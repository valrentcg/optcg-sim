// One Piece TCG — Sealed / Pre-Release: the EVENT screens (Swiss rounds and standings).
//
// Models an actual local: everyone opens, everyone builds against a clock, then Swiss rounds with
// standings between them and deck edits allowed between rounds (the POOL never changes — only which
// 40 you register). All pairing, byes, OMW tiebreaks and A.I.-vs-A.I. resolution live in SealedEvent
// (pure C#, tested); this file only draws it.

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public sealed class SealedEventUI : MonoBehaviour
    {
        private RectTransform root, body;
        private SealedEvent ev;
        private SealedPool humanPool;
        private Action onExit;
        private Action<SealedPool, SealedPool> onPlayMatch;   // player deck, opponent deck

        public void Begin(RectTransform parent, SealedEvent evt, SealedPool pool,
            Action<SealedPool, SealedPool> playMatch, Action exit)
        {
            root = parent;
            ev = evt;
            humanPool = pool;
            onPlayMatch = playMatch;
            onExit = exit;
            Render();
        }

        /// <summary>Report the human's result and advance. Called by the host after a match ends.</summary>
        public void ReportHumanResult(bool won)
        {
            var mine = ev.CurrentPairingFor("player");
            if (mine != null && !mine.Reported) ev.Report(mine, won ? "player" : (mine.AId == "player" ? mine.BId : mine.AId));
            ev.ResolveAiPairings();
            Render();
        }

        private void Render()
        {
            if (body != null) Destroy(body.gameObject);
            body = SealedUI.Panel(root, "Event", SealedUI.PanelBg);
            SealedUI.Fill(body);

            var title = SealedUI.Label(body, "T",
                ev.Phase == EventPhase.Finished ? "FINAL STANDINGS" : $"ROUND {ev.CurrentRound} / {ev.TotalRounds}",
                28, SealedUI.Ink, TextAnchor.UpperLeft, true);
            SealedUI.Stretch(title.rectTransform, new Vector2(0.04f, 0.90f), new Vector2(0.6f, 0.97f));

            var sub = SealedUI.Label(body, "S",
                $"{ev.SetCode} · seed {ev.Seed} · {ev.PlayerCount} players · {SealedLeaderRules.ModeName(humanPool.LeaderMode)}",
                12, SealedUI.Muted, TextAnchor.UpperLeft);
            SealedUI.Stretch(sub.rectTransform, new Vector2(0.04f, 0.855f), new Vector2(0.7f, 0.90f));

            SealedUI.Button(body, "◂ LEAVE", SealedUI.ChipOff, SealedUI.Ink, () => { Cleanup(); onExit?.Invoke(); })
                .let(rt => SealedUI.Stretch(rt, new Vector2(0.90f, 0.915f), new Vector2(0.97f, 0.96f)));

            BuildStandings();
            BuildRoundPanel();
        }

        private void BuildStandings()
        {
            var host = SealedUI.Panel(body, "Standings", SealedUI.PanelBg2);
            SealedUI.Stretch(host, new Vector2(0.04f, 0.08f), new Vector2(0.50f, 0.84f));
            var col = SealedUI.ScrollColumn(host, "St", 3f);
            SealedUI.Stretch(col.parent as RectTransform, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.98f));

            SealedUI.Label(col, "H", "STANDINGS", 12, SealedUI.Accent, TextAnchor.MiddleLeft, true)
                .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            foreach (var s in ev.Standings())
            {
                bool me = s.Entrant.IsHuman;
                var row = SealedUI.Panel(col, "Row", me ? new Color32(28, 60, 78, 235) : new Color(0, 0, 0, 0));
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

                var t = SealedUI.Label(row, "L",
                    $"{s.Rank,2}.  {s.Entrant.DisplayName,-16}  {ev.RecordText(s.Entrant),-7}  {s.Entrant.MatchPoints,2} pts"
                    + (s.Entrant.Byes > 0 ? "  (bye)" : ""),
                    12, me ? SealedUI.Accent : SealedUI.Ink, TextAnchor.MiddleLeft, me);
                SealedUI.Stretch(t.rectTransform, new Vector2(0.02f, 0f), new Vector2(0.98f, 1f));
            }

            SealedUI.Label(col, "F", "Ties broken by opponents' win % (OMW), as at a real event.",
                10, SealedUI.Muted, TextAnchor.UpperLeft)
                .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        }

        private void BuildRoundPanel()
        {
            var host = SealedUI.Panel(body, "Round", SealedUI.PanelBg2);
            SealedUI.Stretch(host, new Vector2(0.52f, 0.08f), new Vector2(0.96f, 0.84f));
            var col = SealedUI.ScrollColumn(host, "Rd", 6f);
            SealedUI.Stretch(col.parent as RectTransform, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.98f));

            void Head(string t) => SealedUI.Label(col, "H", t, 12, SealedUI.Accent, TextAnchor.MiddleLeft, true)
                .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            void Line(string t, Color c, int size = 12) =>
                SealedUI.Label(col, "L", t, size, c, TextAnchor.UpperLeft)
                    .rectTransform.gameObject.AddComponent<LayoutElement>().preferredHeight = size + 10f;
            void Act(string label, Action a, bool primary = true)
            {
                var b = SealedUI.Button(col, label,
                    primary ? SealedUI.Accent : SealedUI.ChipOff,
                    primary ? SealedUI.BadgeInk : SealedUI.Ink, a, 13);
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
            }

            if (ev.Phase == EventPhase.Finished)
            {
                var me = ev.Human();
                var place = ev.Standings().FirstOrDefault(s => s.Entrant.IsHuman);
                Head("EVENT COMPLETE");
                Line($"You finished {Ordinal(place?.Rank ?? 0)} of {ev.PlayerCount} at {ev.RecordText(me)}.",
                    SealedUI.Ink, 14);
                Line("Your pool and deck are saved — you can revisit them any time from the Sealed menu.", SealedUI.Muted, 11);
                Act("BACK TO POOL", () => { Cleanup(); onExit?.Invoke(); });
                return;
            }

            var pairing = ev.CurrentPairingFor("player");
            if (pairing == null) { Line("Waiting for pairings…", SealedUI.Muted); return; }

            if (pairing.IsBye)
            {
                Head("ROUND " + ev.CurrentRound);
                Line("You have the bye this round — counted as a win, as at a real event.", SealedUI.Good, 13);
                Act("CONTINUE", () => { ev.ResolveAiPairings(); AdvanceIfDone(); });
                return;
            }

            var oppId = pairing.AId == "player" ? pairing.BId : pairing.AId;
            var opp = ev.Get(oppId);

            Head("YOUR MATCH");
            Line($"vs  {opp?.DisplayName}   ({ev.RecordText(opp)})", SealedUI.Ink, 15);

            var v = humanPool.Validate();
            if (!v.Ok)
            {
                Line("Your deck is not legal yet: " + v.Problems.FirstOrDefault(), SealedUI.Bad, 11);
                Act("EDIT DECK", () => { Cleanup(); onExit?.Invoke(); });
                return;
            }

            if (!pairing.Reported)
            {
                Act("PLAY MATCH", () => onPlayMatch?.Invoke(humanPool, opp?.Pool));
                Line("Or report the result yourself if you played it elsewhere:", SealedUI.Muted, 10);
                Act("I WON", () => ReportHumanResult(true), false);
                Act("I LOST", () => ReportHumanResult(false), false);
            }
            else
            {
                bool won = pairing.WinnerId == "player";
                Line(won ? "You won this round." : "You lost this round.", won ? SealedUI.Good : SealedUI.Bad, 14);
            }

            if (ev.Phase == EventPhase.RoundComplete)
            {
                Head("BETWEEN ROUNDS");
                Line("You may change your 40 before the next round. Your pool never changes.", SealedUI.Muted, 11);
                Act("EDIT DECK", () => { Cleanup(); onExit?.Invoke(); }, false);
                Act("NEXT ROUND", () => { ev.NextRound(); Render(); });
            }
        }

        private void AdvanceIfDone()
        {
            if (ev.Phase == EventPhase.RoundComplete) ev.NextRound();
            Render();
        }

        private static string Ordinal(int n) =>
            n <= 0 ? "—" :
            (n % 100 is 11 or 12 or 13) ? n + "th" :
            (n % 10) switch { 1 => n + "st", 2 => n + "nd", 3 => n + "rd", _ => n + "th" };

        private void Cleanup()
        {
            if (body != null) Destroy(body.gameObject);
        }
    }
}
