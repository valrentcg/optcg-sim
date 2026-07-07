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
using UnityEngine;
using Velopack;

public static class VelopackBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
        VelopackApp.Build().Run();
#endif
    }
}
