using System;
using System.Collections.Generic;

namespace OnePieceTcg.Engine.Puzzles
{
    /// <summary>
    /// A small authored curriculum. These are strategic positions, not generated seeds or card-identity
    /// variants. Hidden-hand combat exercises use the all-2K worst case, so their answer is derivable from
    /// visible hand count without revealing the opponent's cards.
    /// </summary>
    public static class GoldStandardPuzzleLibrary
    {
        private const string P4 = "ST01-009";       // vanilla 4000
        private const string P5 = "EB01-025";       // vanilla 5000
        private const string P6 = "ST01-010";       // vanilla 6000
        private const string C2 = "OP11-045";       // vanilla +2000 Counter
        private const string Wall = "OP02-108";     // unconditional Blocker
        private const string Event6 = "OP07-095";   // Iron Body: +6000 with 10 trash

        private sealed class Spec
        {
            public string Id, Title, Category, Objective, Teaches, Public, Tags;
            public int Difficulty, Hand, Life, Blockers, MineDon, OppDon, OppTrash;
            public bool FaceUpLife;
            public int CounterEvents;
            public string[] Attackers;
        }

        private static readonly Spec[] CombatSpecs =
        {
            S("gold-cross-seven", "Cross the Seven", "counter-card-floor", 2,
                "Win at 0 Life against three hidden hand cards.",
                "Three ordinary swings ask for one card each. Put both DON!! on one attacker so 7K asks for two 2K counters.",
                "Worst-case objective: any of the 3 hidden cards may be +2000 Counter.", "5,5,5", 3, 0, 0, 2,
                "Hand count|7K breakpoint|Card demand|DON!! concentration"),
            S("gold-natural-six", "One DON!!, Two Cards", "natural-power-breakpoint", 1,
                "Find the single attachment that makes three cards insufficient.",
                "A natural 6K body is one DON!! away from 7K; that step changes its defense from one 2K card to two.",
                "Worst-case objective: any of the 3 hidden cards may be +2000 Counter.", "6,5,5", 3, 0, 0, 1,
                "Natural power|7K breakpoint|Card demand"),
            S("gold-repair-the-four", "Don't Just Make It Connect", "low-body-breakpoint", 3,
                "Make the 4K attacker consume two cards, not merely become a legal swing.",
                "Raising 4K to 5K only creates a one-card demand. All three DON!! belong there so it reaches 7K.",
                "Worst-case objective: any of the 3 hidden cards may be +2000 Counter.", "4,5,6", 3, 0, 0, 3,
                "Low-power attacker|7K breakpoint|Card efficiency|DON!! concentration"),
            S("gold-split-the-sixes", "Two Separate Problems", "double-breakpoint-split", 3,
                "Make four hidden cards fail to cover three attacks.",
                "One DON!! on each natural 6K creates two separate 7K demands. Stacking both still costs only two cards.",
                "Worst-case objective: any of the 4 hidden cards may be +2000 Counter.", "6,6,5", 4, 0, 0, 2,
                "Two breakpoints|Card demand|Exact DON!! split"),
            S("gold-three-sevens", "Three Sevens", "triple-breakpoint-split", 4,
                "Turn five hidden cards into an insufficient defense.",
                "Each natural 6K needs exactly one DON!!. Three 7K attacks demand six cards; a lopsided split drops the demand to five.",
                "Worst-case objective: any of the 5 hidden cards may be +2000 Counter.", "6,6,6", 5, 0, 0, 3,
                "Three breakpoints|Six-card demand|Exact DON!! split"),
            S("gold-fourth-demand", "The Fifth Card They Don't Have", "wide-card-floor", 2,
                "Break a four-card hand with four attackers.",
                "Four 5K swings match four counters. Raising one attacker to 7K creates the fifth card demand.",
                "Worst-case objective: any of the 4 hidden cards may be +2000 Counter.", "5,5,5,5", 4, 0, 0, 2,
                "Hand count|Wide board|7K breakpoint"),
            S("gold-four-jobs", "Four Different Jobs", "mixed-power-exact-split", 4,
                "Use four DON!! so six hidden cards cannot cover four differently powered attackers.",
                "The 4K body needs one DON!! to matter; each 6K needs one to cross 7K. Extra DON!! on one leaves another below threshold.",
                "Worst-case objective: any of the 6 hidden cards may be +2000 Counter.", "4,6,6,6", 6, 0, 0, 4,
                "Mixed base power|Multiple breakpoints|Exact allocation|Card demand"),
            S("gold-blocker-equalize", "Don't Feed the Blocker", "blocker-pressure-equalization", 3,
                "Win through one Blocker and two hidden hand cards.",
                "Two 7K attacks deny the Blocker one obvious cannon. The unblocked 7K consumes both counters.",
                "One attack can be blocked; plan as if either hidden card could be +2000 Counter.", "5,5,5", 2, 0, 1, 4,
                "Blocker assignment|Pressure equalization|Card demand|Attack order"),
            S("gold-blocker-two-sixes", "Raise Both Swords", "blocker-double-breakpoint", 3,
                "Make one Blocker and two counters insufficient.",
                "Raise both natural 6Ks to 7K. The Blocker can erase one expensive demand, but the other consumes both cards.",
                "One attack can be blocked; plan as if either hidden card could be +2000 Counter.", "6,6,5", 2, 0, 1, 2,
                "Blocker assignment|Two breakpoints|Exact DON!! split"),
            S("gold-blocker-four-wide", "Make the Blocker Choose", "blocker-wide-equalization", 4,
                "Win through a Blocker and a three-card hand without building one disposable cannon.",
                "Create two 7K attacks. After one is blocked, the other consumes two cards and two 5Ks exceed the last card.",
                "One attack can be blocked; plan for +2000 Counter from any hidden card.", "5,5,5,5", 3, 0, 1, 4,
                "Blocker assignment|Pressure equalization|Five-card demand"),
            S("gold-two-blockers", "Three Threats, Two Walls", "double-blocker-thresholds", 4,
                "Win through two Blockers and one hidden counter.",
                "All three different attackers must become 7K. Two can be blocked; the last demands two counter cards.",
                "Two attacks can be blocked; plan for the hidden card to be +2000 Counter.", "4,5,6", 1, 0, 2, 6,
                "Two Blockers|Three breakpoints|Defense assignment"),
            S("gold-life-weak-first", "Let Life Make the Mistake", "life-weak-to-strong", 1,
                "Defeat the opponent through one face-up Life card.",
                "Attack for 5K before 7K. Taking the first hit draws a 2K counter that is too small to stop 7K.",
                "The opponent's only Life is face-up and has +2000 Counter.", "5,5", 0, 1, 0, 2,
                "Life-to-hand|Weak-to-strong order|7K breakpoint", faceUp: true),
            S("gold-life-and-hand", "Two Medium Demands", "life-hand-sequencing", 3,
                "Break one Life plus one hidden hand card.",
                "Lead with 5K, then present two 7Ks. Taking or countering first cannot make the final 7K affordable.",
                "The face-up Life is +2000 Counter; plan for the hidden card to be +2000 as well.", "5,5,5", 1, 1, 0, 4,
                "Life-to-hand|Response-dependent sequencing|Repeated 7K demands", faceUp: true),
            S("gold-two-life-ladder", "Build the Ladder", "two-life-sequencing", 3,
                "Find three successful hits through two face-up Life cards.",
                "The 5K attacks belong first and the 7K attacks last. Life cards gained early cannot cover both later demands.",
                "Both face-up Life cards have +2000 Counter.", "5,5,5,5", 0, 2, 0, 4,
                "Two Life|Weak-to-strong ladder|Card demand after draws", faceUp: true),
            S("gold-life-blocker", "Life Behind a Wall", "life-blocker-sequencing", 4,
                "Win through one face-up Life and one Blocker.",
                "Build two 7K demands from different base powers. The Blocker can erase one, but the Life card cannot cover the other.",
                "The face-up Life has +2000 Counter; one attack can be blocked.", "4,5,6", 0, 1, 1, 4,
                "Life-to-hand|Blocker assignment|Mixed thresholds|Attack order", faceUp: true),
            S("gold-life-wide-read", "Count Future Hand Cards", "life-future-hand-count", 4,
                "Win through two Life and one hidden hand card.",
                "Plan against the hand they will have after taking Life. Repeated 7K demands make those future cards inefficient.",
                "Both face-up Life cards are +2000 Counter; plan for the hidden card to be +2000 as well.", "5,5,5,5,5", 1, 2, 0, 6,
                "Future hand size|Two Life|Repeated breakpoints|Attack order", faceUp: true),
            S("gold-life-blocker-hand", "Every Defense Has a Job", "life-blocker-hand-policy", 4,
                "Win through a Life card, a Blocker, and a hidden counter.",
                "Build two separate 7K demands. One is blocked; the other consumes both counter cards, leaving two attacks for Life and lethal.",
                "Face-up Life is +2000; plan for +2000 from the hidden card, and one attack can be blocked.", "5,6,5,5", 1, 1, 1, 3,
                "Layered defense|Life-to-hand|Blocker assignment|Card demand", faceUp: true),
            S("gold-iron-body-ceiling", "Above Iron Body", "counter-event-ceiling", 3,
                "Win at 0 Life through two copies of Iron Body.",
                "Only the natural 6K attacker can reach 11K with five DON!!. Each event reaches 11K defense, and matching power still connects.",
                "Worst-case objective: respect two +6000 Iron Body events; 4 active DON!! can pay for both.", "5,6", 0, 0, 0, 5,
                "Counter Events|Active DON!!|11K ceiling|DON!! concentration", oppDon: 4, trash: 10, events: 2),
            S("gold-event-plus-card", "Price Both Defenses", "counter-event-and-hand", 4,
                "Win through two Iron Body events plus one 2K counter.",
                "Raise the natural 6K attacker to 11K. Stopping it requires combining defenses, so another attack is left uncovered.",
                "Worst-case objective: respect two +6000 Iron Body events plus another possible +2000 counter.", "5,6,5", 1, 0, 0, 5,
                "Counter Events|Hidden counter|11K ceiling|Defense assignment", oppDon: 4, trash: 10, events: 2),
            S("gold-low-body-life", "A 4K Can Be the Finisher", "low-body-life-ladder", 3,
                "Win through one face-up Life and one hidden counter with a 4K, 5K, and 6K attacker.",
                "Build 5K, 7K, and 7K from three different bases, then order them around the two counter cards Life can create.",
                "The face-up Life is +2000 Counter; plan for the hidden card to be +2000 as well.", "4,5,6", 1, 1, 0, 4,
                "Mixed base power|Life-to-hand|Connection threshold|Attack order", faceUp: true),
            S("gold-combat-final", "Combat Math Final", "combat-policy-final", 4,
                "Win through one Life, two hidden counters, and one Blocker.",
                "Count every defense, including the Life draw. Use the natural 6Ks for multiple 7K demands, not one blockable cannon.",
                "Face-up Life is +2000; plan for +2000 from either hidden card, and one attack can be blocked.", "6,6,5,5,5", 2, 1, 1, 6,
                "Hand count|Life-to-hand|Blocker assignment|Multiple breakpoints", faceUp: true),
        };

        public static List<AuthoredPuzzle> All()
        {
            var result = new List<AuthoredPuzzle>();
            foreach (var spec in CombatSpecs) result.Add(Combat(spec));
            result.Add(Synth(3, "gold-exact-split-decoy", "No Room for Error", "exact-split-with-decoy", 3,
                "A playable card is a resource trap. Preserve the exact DON!! split.", "DON!! budgeting|Decoy play|Exact split|Hidden Counter"));
            result.Add(Synth(4, "gold-wall-split-decoy", "The Narrow Path", "wall-and-split", 4,
                "Neutralize the Blocker without spending the DON!! needed for both attacks.", "Blocker removal|DON!! budgeting|Decoy play|Hidden Counter"));
            result.Add(Synth(1, "gold-life-counter-order", "Read the Whole Position", "known-life-counter-order", 3,
                "Build the second attack for the card the first hit will draw from Life.", "Life-to-hand|Attack order|Known future Counter|DON!! allocation"));
            result.Add(Synth(2, "gold-rush-event-gate", "One Chance", "rush-counter-event-gate", 4,
                "Pay for Rush, disable the Blocker, and cross the Counter Event threshold.", "Rush|Blocker suppression|Counter Event|Exact DON!! budget"));
            return result;
        }

        private static Spec S(string id, string title, string category, int difficulty, string objective,
            string teaches, string publicInfo, string attackers, int hand, int life, int blockers, int mineDon,
            string tags, bool faceUp = false, int oppDon = 0, int trash = 0, int events = 0) => new Spec
        {
            Id = id, Title = title, Category = category, Difficulty = difficulty, Objective = objective,
            Teaches = teaches, Public = publicInfo, Tags = tags, Attackers = attackers.Split(','),
            Hand = hand, Life = life, Blockers = blockers, MineDon = mineDon, FaceUpLife = faceUp,
            OppDon = oppDon, OppTrash = trash, CounterEvents = events,
        };

        private static AuthoredPuzzle Combat(Spec s)
        {
            var grade = Grade(s.Difficulty);
            return new AuthoredPuzzle
            {
                Id = s.Id, Title = s.Title, Category = s.Category, Difficulty = grade.tier,
                DifficultyScore = grade.score, DifficultyEvidence = grade.evidence,
                Objective = s.Objective, PublicInformation = s.Public, Teaches = s.Teaches,
                Mechanics = s.Tags.Split('|'),
                Build = () =>
                {
                    var b = new Builder(s.Id);
                    foreach (string p in s.Attackers)
                        b.Character("south", p == "4" ? P4 : p == "6" ? P6 : P5);
                    for (int i = 0; i < s.Hand; i++) b.Hand("north", C2);
                    for (int i = 0; i < s.Life; i++) b.Life("north", C2, s.FaceUpLife);
                    for (int i = 0; i < s.Blockers; i++) b.Character("north", Wall);
                    for (int i = 0; i < s.CounterEvents; i++) b.Hand("north", Event6);
                    if (s.OppTrash > 0) b.Trash("north", "EB03-026", s.OppTrash);
                    b.Don("south", s.MineDon);
                    b.Don("north", s.OppDon);
                    return b.State;
                },
            };
        }

        private static AuthoredPuzzle Synth(int seed, string id, string title, string category, int difficulty,
            string objective, string tags)
        {
            var grade = Grade(difficulty);
            return new AuthoredPuzzle
            {
                Id = id, Title = title, Category = category, Difficulty = grade.tier,
                DifficultyScore = grade.score, DifficultyEvidence = grade.evidence,
                Objective = objective,
                PublicInformation = "Use visible Life, hand count, board, and active DON!!; hand identities stay hidden.",
                Mechanics = tags.Split('|'),
                Teaches = PuzzleSynthesizer.Build(seed).Teaches,
                Build = () => PuzzleSynthesizer.Build(seed).State,
            };
        }

        private static (int tier, double score, string evidence) Grade(int tier) => tier switch
        {
            1 => (1, 7.50, "one visible threshold with no hidden-composition dependency"),
            2 => (2, 9.25, "one card-consumption breakpoint plus a tempting inefficient allocation"),
            3 => (3, 10.50, "multiple interacting attack, hand, Life, or DON!! thresholds"),
            _ => (4, 11.75, "layered defense assignment with response-dependent attack decisions"),
        };

        private sealed class Builder
        {
            private int _next;
            public GameState State { get; }

            public Builder(string seed)
            {
                State = GameEngine.CreateMatch(new MatchConfig
                {
                    SouthDeck = "st07", NorthDeck = "st03", Seed = seed,
                });
                State.Status = "active"; State.Phase = "main"; State.ActiveSeat = "south";
                State.TurnNumber = 8; State.Battle = null; State.ActiveChoice = null;
                State.DeckLook = null; State.PendingEffects.Clear(); State.Selected = null;
                foreach (string seat in new[] { "south", "north" })
                {
                    var p = State.Players[seat];
                    p.TurnsStarted = 4; p.Hand.Clear(); p.Life.Clear(); p.Trash.Clear();
                    p.CostArea.Clear(); p.Stage = null; p.DonDeck = 0; p.Deck.Clear();
                    for (int i = 0; i < 8; i++) p.Deck.Add(Card("EB03-026", seat, "deck"));
                    for (int i = 0; i < p.CharacterArea.Count; i++) p.CharacterArea[i] = null;
                    p.Leader.CardId = seat == "south" ? "ST07-001" : "ST03-001";
                    p.Leader.Rested = seat == "south"; p.Leader.PlayedOnTurn = null;
                    p.Leader.AttachedDonIds.Clear();
                }
            }

            public void Character(string seat, string id)
            {
                var p = State.Players[seat];
                for (int i = 0; i < p.CharacterArea.Count; i++)
                    if (p.CharacterArea[i] == null) { p.CharacterArea[i] = Card(id, seat, "character"); return; }
                throw new InvalidOperationException("No open Character slot.");
            }
            public void Hand(string seat, string id) => State.Players[seat].Hand.Add(Card(id, seat, "hand"));
            public void Life(string seat, string id, bool faceUp)
            {
                var c = Card(id, seat, "life"); c.FaceUp = faceUp; State.Players[seat].Life.Add(c);
            }
            public void Trash(string seat, string id, int n)
            {
                for (int i = 0; i < n; i++) State.Players[seat].Trash.Add(Card(id, seat, "trash"));
            }
            public void Don(string seat, int n)
            {
                for (int i = 0; i < n; i++)
                    State.Players[seat].CostArea.Add(new DonInstance { InstanceId = Next(seat + "-don") });
            }
            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = Next(owner + "-" + id), CardId = id, Owner = owner, Zone = zone,
                Rested = false, PlayedOnTurn = null,
            };
            private string Next(string prefix) => $"{prefix}-{_next++}";
        }
    }
}
