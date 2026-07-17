using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// A LIVE progress board for long self-play runs. Each running arm registers itself and calls
    /// <see cref="Tick"/> once per finished game; the board persists that arm's state to a per-run JSON file
    /// and regenerates a single self-contained <c>dashboard.html</c> that auto-refreshes every second.
    ///
    /// WHY a rewritten HTML file and not a socket or an in-terminal bar: a backgrounded <c>dotnet</c> process
    /// buffers stdout until it exits, so there is no live terminal progress; and a browser cannot poll a local
    /// process (or <c>fetch</c> a sibling file over <c>file://</c>). Baking the current numbers straight into an
    /// auto-refreshing HTML file sidesteps both — open <c>dashboard.html</c> once and the bars fill as games land.
    ///
    /// Every method is best-effort and swallows IO errors: a progress visual must never be able to fail a run.
    /// The board reads ALL <c>*.run.json</c> in the directory when it renders, so several concurrent processes
    /// (separate background commands) show up as separate bars on the same page.
    /// </summary>
    public static class ProgressBoard
    {
        // Outcome codes match HonestMutationPilot: 1=win, 0=loss, <0=invalid (stuck/throw/tie).
        private sealed class RunState
        {
            public string RunId { get; set; }
            public string Label { get; set; }
            public int Total { get; set; }
            public int Won { get; set; }
            public int Lost { get; set; }
            public int Invalid { get; set; }
            public long StartUnixMs { get; set; }
            public long UpdateUnixMs { get; set; }
            public bool Finished { get; set; }
            public int Done => Won + Lost + Invalid;
        }

        private static readonly ConcurrentDictionary<string, RunState> Local = new();
        private static readonly object RenderLock = new();
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        private static string Dir =>
            Path.Combine(Directory.GetCurrentDirectory(), "Results", "progress");

        /// <summary>Absolute path to the file the user opens in a browser.</summary>
        public static string DashboardPath => Path.Combine(Dir, "dashboard.html");

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Register a run (an arm of an experiment) and draw its empty bar. Returns the runId to pass
        /// back to <see cref="Tick"/>/<see cref="Finish"/>. A process-unique suffix keeps concurrent commands
        /// from colliding on the same file.</summary>
        public static string Start(string key, string label, int total)
        {
            string runId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Sanitize(key)}";
            var st = new RunState
            {
                RunId = runId, Label = label, Total = Math.Max(0, total),
                StartUnixMs = NowMs(), UpdateUnixMs = NowMs(),
            };
            Local[runId] = st;
            Persist(st);
            Render();
            return runId;
        }

        /// <summary>Record one finished game and repaint. Thread-safe: several worker threads of one
        /// <c>Parallel.For</c> tick the same run concurrently.</summary>
        public static void Tick(string runId, int outcome)
        {
            if (runId == null || !Local.TryGetValue(runId, out var st)) return;
            lock (st)
            {
                if (outcome > 0) st.Won++;
                else if (outcome == 0) st.Lost++;
                else st.Invalid++;
                st.UpdateUnixMs = NowMs();
            }
            Persist(st);
            Render();
        }

        /// <summary>Mark a run complete (its bar stops shimmering and shows a final pill).</summary>
        public static void Finish(string runId)
        {
            if (runId == null || !Local.TryGetValue(runId, out var st)) return;
            lock (st) { st.Finished = true; st.UpdateUnixMs = NowMs(); }
            Persist(st);
            Render();
        }

        private static void Persist(RunState st)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, st.RunId + ".run.json");
                string tmp = path + ".tmp";
                lock (st) File.WriteAllText(tmp, JsonSerializer.Serialize(st, Json));
                File.Move(tmp, path, overwrite: true); // atomic-ish swap so a reader never sees a torn file
            }
            catch { /* progress must never fail the run */ }
        }

        private static void Render()
        {
            lock (RenderLock)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    var runs = LoadAll();
                    string html = BuildHtml(runs);
                    string tmp = DashboardPath + ".tmp";
                    File.WriteAllText(tmp, html, new UTF8Encoding(false));
                    File.Move(tmp, DashboardPath, overwrite: true);
                }
                catch { /* best effort */ }
            }
        }

        private static RunState[] LoadAll()
        {
            var byId = new System.Collections.Generic.Dictionary<string, RunState>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(Dir, "*.run.json"))
                {
                    try
                    {
                        var st = JsonSerializer.Deserialize<RunState>(File.ReadAllText(file));
                        if (st?.RunId != null) byId[st.RunId] = st;
                    }
                    catch { /* skip a file mid-write */ }
                }
            }
            catch { /* dir may not exist yet */ }
            // In-process state is the freshest source of truth for our own runs.
            foreach (var st in Local.Values) byId[st.RunId] = st;
            return byId.Values.OrderBy(r => r.StartUnixMs).ToArray();
        }

        private static string Sanitize(string s) =>
            new string((s ?? "run").Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

        private static string FmtDuration(long ms)
        {
            if (ms < 0) ms = 0;
            var t = TimeSpan.FromMilliseconds(ms);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds:00}s";
            return $"{t.Seconds}s";
        }

        private static string BuildHtml(RunState[] runs)
        {
            long now = NowMs();
            int totalGames = runs.Sum(r => r.Total);
            int doneGames = runs.Sum(r => r.Done);
            int activeRuns = runs.Count(r => !r.Finished && (now - r.UpdateUnixMs) < 120_000);
            double overallPct = totalGames > 0 ? 100.0 * doneGames / totalGames : 0;
            bool anyActive = activeRuns > 0;

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            // Only keep hammering the reload while something is still running; a settled board holds still.
            if (anyActive) sb.Append("<meta http-equiv=\"refresh\" content=\"1\">");
            sb.Append("<title>Self-play progress</title>");
            sb.Append("<style>").Append(Css()).Append("</style></head><body>");

            sb.Append("<header class=\"top\"><div class=\"brand\">")
              .Append("<span class=\"mark\" aria-hidden=\"true\"></span>")
              .Append("<div><h1>Self-play progress</h1>")
              .Append("<p class=\"sub\">honest OPTCG bot &mdash; games completing across every running arm</p></div></div>");
            sb.Append("<div class=\"summary\">");
            sb.Append(Stat("runs", runs.Length.ToString(CultureInfo.InvariantCulture)));
            sb.Append(Stat("running", activeRuns.ToString(CultureInfo.InvariantCulture)));
            sb.Append(Stat("games", $"{doneGames}<span class=\"of\">/{totalGames}</span>"));
            sb.Append(Stat("complete", $"{overallPct:0}<span class=\"of\">%</span>"));
            sb.Append("</div></header>");

            sb.Append("<main class=\"board\">");
            if (runs.Length == 0)
                sb.Append("<p class=\"empty\">No runs have registered yet. Start a self-play command and its bar appears here.</p>");
            foreach (var r in runs) sb.Append(Card(r, now));
            sb.Append("</main>");

            sb.Append("<footer class=\"foot\"><span class=\"dot ")
              .Append(anyActive ? "live" : "idle").Append("\"></span>")
              .Append(anyActive ? "Live &mdash; refreshing every second" : "Idle &mdash; all runs settled")
              .Append("<span class=\"clock\">updated ").Append(DateTime.Now.ToString("HH:mm:ss"))
              .Append("</span></footer>");

            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string Stat(string label, string value) =>
            $"<div class=\"stat\"><span class=\"v\">{value}</span><span class=\"k\">{label}</span></div>";

        private static string Card(RunState r, long now)
        {
            int total = Math.Max(r.Total, r.Done);
            double pct = total > 0 ? 100.0 * r.Done / total : 0;
            double wonPct = total > 0 ? 100.0 * r.Won / total : 0;
            double lostPct = total > 0 ? 100.0 * r.Lost / total : 0;
            double invPct = total > 0 ? 100.0 * r.Invalid / total : 0;

            long elapsed = Math.Max(0, r.UpdateUnixMs - r.StartUnixMs);
            bool stalled = !r.Finished && (now - r.UpdateUnixMs) >= 120_000;
            bool running = !r.Finished && !stalled;

            string eta;
            if (r.Finished) eta = "done";
            else if (stalled) eta = "stalled";
            else if (r.Done > 0 && elapsed > 0)
            {
                double perGame = (double)elapsed / r.Done;
                long remMs = (long)(perGame * (total - r.Done));
                eta = "~" + FmtDuration(remMs) + " left";
            }
            else eta = "starting…";

            string state = r.Finished ? "done" : stalled ? "stalled" : "running";
            int decided = r.Won + r.Lost;
            string rate = decided > 0 ? $"{100.0 * r.Won / decided:0}% win" : "—";

            var sb = new StringBuilder();
            sb.Append("<section class=\"card ").Append(state).Append("\">");
            sb.Append("<div class=\"card-head\"><h2>").Append(WebEncode(r.Label)).Append("</h2>");
            sb.Append("<span class=\"pill ").Append(state).Append("\">").Append(state).Append("</span></div>");

            sb.Append("<div class=\"track\">");
            sb.Append(Seg("won", wonPct));
            sb.Append(Seg("lost", lostPct));
            sb.Append(Seg("inv", invPct));
            if (running) sb.Append("<div class=\"head-glow\" style=\"left:").Append(Pc(pct)).Append("\"></div>");
            sb.Append("<div class=\"pct\">").Append(pct.ToString("0", CultureInfo.InvariantCulture)).Append("%</div>");
            sb.Append("</div>");

            sb.Append("<div class=\"meta\">");
            sb.Append(Chip($"{r.Done}", $"/ {total} games"));
            sb.Append(Chip($"{r.Won}", "won", "won"));
            sb.Append(Chip($"{r.Lost}", "lost", "lost"));
            sb.Append(Chip($"{r.Invalid}", "invalid", "inv"));
            sb.Append(Chip(rate, "decided"));
            sb.Append("<span class=\"spacer\"></span>");
            sb.Append(Chip(FmtDuration(elapsed), "elapsed"));
            sb.Append(Chip(eta, ""));
            sb.Append("</div></section>");
            return sb.ToString();
        }

        private static string Seg(string cls, double pct) =>
            pct <= 0 ? "" : $"<div class=\"seg {cls}\" style=\"width:{Pc(pct)}\"></div>";

        private static string Chip(string value, string label, string tone = "")
        {
            string t = string.IsNullOrEmpty(tone) ? "" : " " + tone;
            string l = string.IsNullOrEmpty(label) ? "" : $"<span class=\"cl\">{label}</span>";
            return $"<span class=\"chip{t}\"><span class=\"cv\">{WebEncode(value)}</span>{l}</span>";
        }

        private static string Pc(double v) => v.ToString("0.###", CultureInfo.InvariantCulture) + "%";

        private static string WebEncode(string s) => (s ?? "")
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // A committed single theme: a dark "sonar / navigator's console" look — deep sea-slate ground, a warm
        // brass accent for the frame, and semantic green/red/steel for game outcomes (kept distinct from brass).
        private static string Css() => @"
:root{
  --ground:#0c1420; --panel:#111c2b; --panel-2:#16273a; --line:#22374f;
  --ink:#e8eef6; --muted:#8ba0b8; --faint:#5c7690;
  --brass:#e0a44c; --brass-soft:#f0c98a;
  --won:#3fbf8f; --lost:#e0576b; --inv:#5b7086; --track:#0a1119;
}
*{box-sizing:border-box}
html,body{margin:0}
body{
  background:radial-gradient(1200px 700px at 78% -10%, #16273a 0%, var(--ground) 60%) fixed;
  color:var(--ink); min-height:100vh; padding:28px clamp(16px,4vw,56px);
  font:15px/1.5 -apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;
  font-variant-numeric:tabular-nums;
}
.top{display:flex;flex-wrap:wrap;gap:20px 32px;align-items:flex-end;justify-content:space-between;
  padding-bottom:20px;margin-bottom:24px;border-bottom:1px solid var(--line)}
.brand{display:flex;gap:16px;align-items:center}
.mark{width:38px;height:38px;border-radius:9px;flex:0 0 auto;
  background:conic-gradient(from 210deg,var(--brass),var(--brass-soft),var(--brass));
  box-shadow:0 0 0 1px #0006 inset,0 6px 18px #e0a44c33;position:relative}
.mark::after{content:'';position:absolute;inset:9px;border-radius:50%;
  border:2px solid #0c1420cc;border-top-color:transparent;animation:spin 3.2s linear infinite}
h1{margin:0;font-size:20px;letter-spacing:-.2px;font-weight:650}
.sub{margin:2px 0 0;color:var(--muted);font-size:13px}
.summary{display:flex;gap:10px;flex-wrap:wrap}
.stat{background:var(--panel);border:1px solid var(--line);border-radius:11px;
  padding:9px 15px;min-width:78px;display:flex;flex-direction:column;gap:1px}
.stat .v{font-size:20px;font-weight:650;letter-spacing:-.3px}
.stat .v .of{color:var(--muted);font-size:13px;font-weight:500}
.stat .k{font-size:11px;text-transform:uppercase;letter-spacing:.12em;color:var(--muted)}
.board{display:flex;flex-direction:column;gap:14px}
.empty{color:var(--muted);padding:40px;text-align:center;border:1px dashed var(--line);border-radius:14px}
.card{background:linear-gradient(180deg,var(--panel) 0%,var(--panel-2) 140%);
  border:1px solid var(--line);border-radius:15px;padding:16px 18px 15px;
  box-shadow:0 1px 0 #ffffff08 inset,0 10px 26px -18px #000a}
.card.running{border-color:#2f4d6e}
.card.done{opacity:.9}
.card.stalled{border-color:#5a3a2a}
.card-head{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:12px}
.card-head h2{margin:0;font-size:15px;font-weight:600;letter-spacing:.1px;
  overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.pill{font-size:10.5px;text-transform:uppercase;letter-spacing:.13em;font-weight:700;
  padding:4px 10px;border-radius:999px;flex:0 0 auto;border:1px solid transparent}
.pill.running{color:#0c1420;background:var(--brass);box-shadow:0 0 14px #e0a44c55}
.pill.done{color:var(--won);border-color:#2c5a49;background:#12271f}
.pill.stalled{color:#f0b483;border-color:#6b452e;background:#2a1c12}
.track{position:relative;height:26px;border-radius:8px;overflow:hidden;
  background:repeating-linear-gradient(90deg,var(--track) 0 11px,#0d1622 11px 12px);
  border:1px solid #0a0f18;display:flex}
.seg{height:100%;transition:width .35s ease}
.seg.won{background:linear-gradient(180deg,#4bd6a1,var(--won))}
.seg.lost{background:linear-gradient(180deg,#ef6d80,var(--lost))}
.seg.inv{background:linear-gradient(180deg,#6d829a,var(--inv))}
.pct{position:absolute;right:9px;top:50%;transform:translateY(-50%);
  font-size:12px;font-weight:700;color:var(--ink);text-shadow:0 1px 3px #000b}
.head-glow{position:absolute;top:0;bottom:0;width:2px;margin-left:-1px;
  background:var(--brass-soft);box-shadow:0 0 12px 3px #f0c98a99;transition:left .35s ease}
.card.running .track::after{content:'';position:absolute;inset:0;
  background:linear-gradient(90deg,transparent,#ffffff14,transparent);
  transform:translateX(-100%);animation:sweep 1.6s ease-in-out infinite}
.meta{display:flex;flex-wrap:wrap;gap:7px;align-items:center;margin-top:11px}
.spacer{flex:1 1 auto}
.chip{display:inline-flex;align-items:baseline;gap:5px;background:#0e1a28;
  border:1px solid var(--line);border-radius:8px;padding:4px 9px;font-size:12px}
.chip .cv{font-weight:650}
.chip .cl{color:var(--muted);font-size:11px}
.chip.won .cv{color:var(--won)} .chip.lost .cv{color:var(--lost)} .chip.inv .cv{color:#93a6bd}
.foot{display:flex;align-items:center;gap:9px;margin-top:22px;padding-top:14px;
  border-top:1px solid var(--line);color:var(--muted);font-size:12.5px}
.foot .clock{margin-left:auto;font-variant-numeric:tabular-nums}
.dot{width:9px;height:9px;border-radius:50%}
.dot.live{background:var(--brass);box-shadow:0 0 0 0 #e0a44c88;animation:pulse 1.4s ease-out infinite}
.dot.idle{background:var(--inv)}
@keyframes spin{to{transform:rotate(360deg)}}
@keyframes sweep{60%,100%{transform:translateX(100%)}}
@keyframes pulse{0%{box-shadow:0 0 0 0 #e0a44c88}70%{box-shadow:0 0 0 7px #e0a44c00}100%{box-shadow:0 0 0 0 #e0a44c00}}
@media (prefers-reduced-motion:reduce){*{animation:none!important;transition:none!important}}
";
    }
}
