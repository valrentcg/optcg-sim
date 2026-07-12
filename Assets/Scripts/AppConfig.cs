// Central app configuration. Secrets are NOT stored in source.
//
// The real shared app-secret (sent as X-App-Secret to the Cloudflare workers)
// lives in a gitignored partial, AppConfig.Local.cs, which exists on build
// machines and is bundled into builds, but is never committed to the public repo.
// A clean checkout still compiles — the partial method is simply elided and the
// secret reads as empty (the workers then reject the request), so nothing here
// leaks the credential into git.
//
// To provision a build machine, create Assets/Scripts/AppConfig.Local.cs:
//
//   public static partial class AppConfig
//   {
//       static partial void InitLocal() { _appSecret = "<the APP_SECRET value>"; }
//   }

public static partial class AppConfig
{
    private static string _appSecret = "";

    /// <summary>Shared app secret for the Cloudflare workers' X-App-Secret gate.
    /// Empty unless a gitignored AppConfig.Local.cs supplies it.</summary>
    public static string AppSecret => _appSecret;

    static AppConfig() { InitLocal(); }

    // Implemented only by the gitignored AppConfig.Local.cs on build machines.
    static partial void InitLocal();
}
