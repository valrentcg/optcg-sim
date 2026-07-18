// One Piece TCG - Velopack lifecycle bootstrap.
//
// Velopack's own docs say VelopackApp.Build().Run() must be the first code
// executed in Main(), so that when Windows launches this exe with a hidden
// install/update/uninstall hook argument, Velopack can intercept it and exit
// before any real app logic runs. Unity has no user-controlled Main() -
// SubsystemRegistration is the earliest point Unity's scripting layer
// exposes, so this is the closest equivalent available.
//
// Requires Mono scripting backend: Velopack uses System.Diagnostics.Process
// internally, which does not function under IL2CPP.
//
// WHY THE MANUAL HOOK GUARD BELOW:
// The Velopack installer runs our exe as a lifecycle hook, e.g.
//     "One Piece TCG Simulator.exe" --veloapp-install 1.0.14
// and waits ~30s for a clean (exit-code-0) exit. VelopackApp.Run() is supposed
// to detect that verb and exit, but it reads the args via
// Environment.GetCommandLineArgs().Skip(1) and matches args[0]. On Unity 6.5
// (6000.5.0f1) the player does not surface the installer's custom args to
// Environment.GetCommandLineArgs() the way Velopack's parser expects, so the
// hook is never matched, the whole game boots, Velopack's 30s timer expires,
// it kills the process, and setup shows "Install Partially Succeeded".
//
// Fix: read the true process command line straight from Win32 (GetCommandLineW),
// which is authoritative regardless of how Unity populates the managed args, and
// exit 0 the moment we see a lifecycle-hook verb. These verbs are ONLY passed by
// the installer/updater during install/update/uninstall - a normal player launch
// never has them - so exiting here is safe. Shortcuts and the uninstall registry
// entry are created by the native Velopack installer itself, not by this hook, so
// there is nothing for us to do except exit cleanly.
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Velopack;

public static class VelopackBootstrap
{
    // Lifecycle-hook verbs Velopack (and legacy Squirrel) pass on the command
    // line. We do no per-hook work, so a single fast exit-0 handles them all.
    static readonly string[] HookVerbs =
    {
        "--veloapp-install", "--veloapp-updated", "--veloapp-obsolete", "--veloapp-uninstall",
        "--squirrel-install", "--squirrel-updated", "--squirrel-obsolete", "--squirrel-uninstall",
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetCommandLineW();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        // Ground-truth command line from Win32 - independent of Unity's managed
        // arg surfacing. If setup ran us as a lifecycle hook, exit 0 immediately.
        string cmdLine;
        try { cmdLine = Marshal.PtrToStringUni(GetCommandLineW()) ?? string.Empty; }
        catch { cmdLine = string.Empty; }

        foreach (var verb in HookVerbs)
        {
            if (cmdLine.IndexOf(verb, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Exit 0 so setup reports success. Nothing else to do - the native
                // installer already placed shortcuts and registry entries.
                Environment.Exit(0);
                return;
            }
        }

        // Normal launch: let Velopack set the AppUserModelID and apply any
        // pending update-on-startup as before.
        VelopackApp.Build().Run();
#endif
    }
}
