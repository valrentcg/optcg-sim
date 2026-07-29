using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Guards against a failure that has now cost this project two separate debugging sessions: an
    /// escape sequence surviving into a source file as a literal CONTROL CHARACTER.
    ///
    /// Both times it came from authoring code through a shell heredoc, where "\b" is consumed by the
    /// shell and written out as byte 0x08 instead of the two characters backslash-b. The result is a
    /// regex that can never match, and - this is the part that makes it expensive - editors and file
    /// readers render 0x08 as nothing, so the source displays as @"plays" and reads as correct no
    /// matter how carefully it is inspected. Fifteen of them sat in the sweep tooling and silently
    /// turned an auto-pick detector into a function that always returned false, which was then
    /// reported as a clean result.
    ///
    /// The same class produced the backtick that terminated a JS template literal from inside a GLSL
    /// comment and blanked three shader prototypes.
    ///
    /// Cheap to run, and it fails loudly rather than invisibly.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- hygiene
    /// </summary>
    public static class SourceHygieneTest
    {
        // Control characters that have no business in source. Tab (9), LF (10) and CR (13) are
        // legitimate; everything else below 0x20 means something went wrong upstream of the file.
        private static readonly Dictionary<char, string> Forbidden = new Dictionary<char, string>
        {
            // Written as ESCAPES, never as literal bytes. A file that embeds the very characters it
            // forbids would fail its own check - and would misread in exactly the way described above.
            { '\0', "NUL" }, { '\a', @"BEL (\a)" }, { '\b', @"BS (\b)" },
            { '\v', @"VT (\v)" }, { '\f', @"FF (\f)" }, { '\u001b', "ESC" },
        };

        private static readonly string[] Extensions = { ".cs", ".js", ".html", ".shader", ".ps1", ".sh" };
        private static readonly string[] SkipDirs =
            { "Library", "Temp", "obj", "bin", ".git", "Build", "node_modules", "Logs", "UserSettings" };

        public static int Run()
        {
            Console.WriteLine("=== Source hygiene: no stray control characters ===");

            string root = FindRepoRoot();
            if (root == null)
            {
                Console.WriteLine("  could not locate the repo root - skipping");
                return 0;
            }

            int scanned = 0;
            var offenders = new List<string>();

            foreach (var file in EnumerateSources(root))
            {
                scanned++;
                string text;
                try { text = File.ReadAllText(file); }
                catch (Exception) { continue; }

                foreach (var kv in Forbidden)
                {
                    int at = text.IndexOf(kv.Key);
                    if (at < 0) continue;
                    int line = text.Take(at).Count(ch => ch == '\n') + 1;
                    string rel = file.Substring(root.Length).TrimStart('\\', '/');
                    offenders.Add($"{rel}:{line}  contains {kv.Value} x{text.Count(ch => ch == kv.Key)}");
                }
            }

            Console.WriteLine($"  scanned {scanned} source files");
            if (offenders.Count == 0)
            {
                Console.WriteLine("  PASS  no control characters in any source file");
                return 0;
            }

            Console.WriteLine($"  FAIL  {offenders.Count} file(s) contain control characters:");
            foreach (var o in offenders) Console.WriteLine("    " + o);
            Console.WriteLine();
            Console.WriteLine("  These render as NOTHING in an editor. A regex holding one can never match,");
            Console.WriteLine("  and it will read as correct every time you inspect it. Usual cause: writing");
            Console.WriteLine("  the file through a shell heredoc, where \\b becomes byte 0x08.");
            return 1;
        }

        private static IEnumerable<string> EnumerateSources(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch (Exception) { continue; }
                foreach (var s in subs)
                {
                    string name = Path.GetFileName(s);
                    if (SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    stack.Push(s);
                }
                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch (Exception) { continue; }
                foreach (var f in files)
                    if (Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                        yield return f;
            }
        }

        /// <summary>Walk up from the working directory until the Assets folder appears, so the check
        /// works whether it is run from the repo root or from Tools/Sim.</summary>
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "Assets"))) return dir.FullName;
            return null;
        }
    }
}
