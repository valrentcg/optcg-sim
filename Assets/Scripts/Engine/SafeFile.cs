using System;
using System.IO;
using System.Text;

namespace OnePieceTcg.Engine
{
    /// <summary>
    /// Crash-safe writes for local player data.
    ///
    /// The plain <c>File.WriteAllText(path, ...)</c> pattern truncates the live file first and then
    /// writes into it, so a crash, a power loss, or a full disk mid-write leaves a TRUNCATED file where
    /// the player's data used to be. That matters most for stores keeping everything in ONE aggregate
    /// file — decks and sealed pools — where a single bad write costs the lot, unlike the per-record
    /// stores (replays, logs) where the blast radius is one item.
    ///
    /// <see cref="WriteAtomic"/> writes to a sibling ".tmp" and swaps it into place, so the real file is
    /// only ever replaced by a COMPLETE one, and keeps the previous good copy as ".bak" for
    /// <see cref="ReadWithRecovery"/> to fall back on.
    ///
    /// Lives ENGINE-SIDE with no UnityEngine dependency on purpose: the Unity-only version could not be
    /// exercised by the headless harness, which left the most damaging fix in the batch (data loss on a
    /// corrupt read) as the only one with no test behind it. Unity wires <see cref="Warn"/> to
    /// Debug.LogWarning at boot; the harness leaves it null and asserts behaviour directly.
    /// </summary>
    public static class SafeFile
    {
        /// <summary>Optional sink for diagnostics. Unity sets this to Debug.LogWarning at boot; null in
        /// headless contexts so the engine assembly stays free of UnityEngine.</summary>
        public static Action<string> Warn;

        private static void W(string msg) { try { Warn?.Invoke(msg); } catch { /* never let logging throw */ } }

        public static string TempPathFor(string path)   => path + ".tmp";
        public static string BackupPathFor(string path) => path + ".bak";

        /// <summary>Write <paramref name="contents"/> so the destination is never left half-written.
        /// Returns false (leaving any existing file untouched) if the write could not be completed.</summary>
        public static bool WriteAtomic(string path, string contents)
        {
            string tmp = TempPathFor(path), bak = BackupPathFor(path);
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(tmp, contents, Encoding.UTF8);

                if (File.Exists(path))
                {
                    // Replace keeps the outgoing copy as .bak in one operation. It can fail on some
                    // filesystems (network shares, certain mobile storage), so fall back to a manual
                    // swap that still never leaves the destination missing for long.
                    try { File.Replace(tmp, path, bak); }
                    catch
                    {
                        if (File.Exists(bak)) File.Delete(bak);
                        File.Move(path, bak);
                        File.Move(tmp, path);
                    }
                }
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                W($"SafeFile: atomic write failed for {path}: {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
                return false;
            }
        }

        /// <summary>Read <paramref name="path"/>, falling back to the ".bak" written by
        /// <see cref="WriteAtomic"/> if the main file is missing or unreadable.
        ///
        /// <paramref name="recovered"/> reports the fallback was used; <paramref name="failed"/> that
        /// NOTHING could be read even though a file was present. Callers must treat
        /// <paramref name="failed"/> as "do not save over this" — turning a failed read into an empty
        /// in-memory state and then flushing it is how a corrupt file becomes permanent data loss.</summary>
        public static string ReadWithRecovery(string path, out bool recovered, out bool failed)
        {
            recovered = false;
            failed = false;
            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
            catch (Exception e) { W($"SafeFile: primary read failed for {path}: {e.Message}"); }

            string bak = BackupPathFor(path);
            try
            {
                if (File.Exists(bak))
                {
                    string text = File.ReadAllText(bak);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        recovered = true;
                        W($"SafeFile: recovered {Path.GetFileName(path)} from backup.");
                        return text;
                    }
                }
            }
            catch (Exception e) { W($"SafeFile: backup read failed for {path}: {e.Message}"); }

            // A file existed but produced nothing usable — that is corruption, not a first run.
            failed = File.Exists(path) || File.Exists(bak);
            return null;
        }
    }
}
