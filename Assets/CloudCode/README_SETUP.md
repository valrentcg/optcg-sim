# Accounts & Password Recovery — Setup Checklist

**2026-07-31: this feature was rebuilt on Unity Player Accounts.** Passwords, email
verification and password RESET are now hosted by Unity. We store no passwords, no email
address, and no email→account index. There is no reset token, no admin API call, and no
mail vendor in the recovery path.

## Why it changed

The old design sat on Unity's **Username/Password** identity provider, which has no
built-in "forgot password". Unity's own guidance for that provider is to build a custom
server-authoritative flow against the Player Authentication Admin API — so we did: a
40-char token in Cloud Save, Resend for mail, a service account, and an admin call to
force-set the password. It was ~400 lines of JS, three secrets and a third-party vendor,
and it shipped broken (the admin call pointed at a URL that didn't exist).

**Unity Player Accounts** is a different provider that hosts all of it. Migrating deleted
the entire stack and added email verification, which we never had.

## What the client does now

| Player action | What happens |
|---|---|
| Create Account / Sign In | `PlayerAccountService.StartSignInAsync` → system browser → `SignInWithUnityAsync` |
| Forgot password | Opens `player-login.unity.com/request-reset-password`. **Works without being signed in** |
| Manage account / change password | Opens `PlayerAccountService.AccountPortalUrl` |
| Pick a name | `ClaimUsername` Cloud Code script (unchanged) |

Usernames were deliberately **not** migrated to Unity's Player Names: that feature only
offers `Name#1234`, and our friend lookup depends on globally-unique base names.
`ClaimUsername.js` still owns uniqueness + the profanity filter. Login is email-based and
now fully decoupled from the display name.

## Required setup

### 1. Enable Unity Player Accounts in the dashboard
Dashboard → Authentication → configure **Unity Player Accounts as an identity provider**. The
SDK's own settings page states the prerequisite plainly: *"Unity Player Accounts must be
configured as an identity provider in the dashboard to be used."*

### 2. Get the Client ID into the project — usually AUTOMATIC
Unity Editor → **Project Settings → Services → Unity Player Accounts** (that exact page title).
Per the SDK: *"Once setup, your client id will automatically synchronize when refreshing identity
providers in the editor."* So **refresh identity providers and check whether the `Client ID` field
has filled itself in** — only paste it by hand if it hasn't.
> Without a Client ID, `StartSignInAsync` throws `MissingClientId` and the sign-in button does
> nothing. The client detects that specific case and shows "Accounts aren't set up in this build"
> rather than a generic error — if you see that message, this step is what's missing.

Leave the **`email` scope enabled** (it's on by default). The Settings screen reads the
address and verified-flag from the ID token; with the scope off it degrades to
"Managed by your Unity account" rather than breaking.

### 3. Redirect URIs
Defaults work without configuration:
- Desktop/standalone: `http://localhost:<free port>/callback` (the SDK binds an
  `HttpListener` on a free port)
- Android/iOS: `unitydl://com.unityplayeraccounts.{project-id}`

### 4. Deploy the Cloud Code scripts
`ClaimUsername`, `LookupPlayerByUsername`, `GetUsernamesForPlayers`, and — only if you
want the welcome email — `SendWelcomeEmail`. Endpoint name must equal the file name.

### 5. Resend — OPTIONAL now
The **only** remaining use of Resend is the welcome email. Recovery does not touch it.
If you want it: set `RESEND_API_KEY` and `RESET_SENDER_EMAIL` as Cloud Code secrets.
If you don't, delete `SendWelcomeEmail.js` and its call in `AccountManager.ClaimUsernameAsync`
— nothing else depends on it, and Unity already sends its own verification mail at signup.

### 6. No longer needed — clean these up
- `UNITY_SA_BASIC` secret and the service account behind it → **delete them**
- Cloud Save custom entities `emailRegistry` and `passwordResets` → **delete them**
- The Player Authentication Admin API no longer needs to be enabled for this feature

## Verify end-to-end
1. Fresh launch → gate shows **Create Account** → browser opens Unity's signup page.
2. Complete signup → app returns signed in → gate switches to **Pick a name**.
3. Claim a name → gate closes, name shows in the top bar.
4. Settings → Account & Recovery → shows your email + verified state; **Manage Account**
   opens Unity's portal.
5. Sign out → **Forgot your password?** on the gate → opens Unity's reset page → reset →
   sign in with the new password.
6. Reinstall (or another machine) → sign in → same name, decks and rank.

## Playtest risks specific to this flow
- **The browser round trip is the whole flow.** `StartSignInAsync` returns as soon as the
  browser launches; completion arrives on the `SignedIn` event. The UI shows
  "Waiting for your browser..." with a **Cancel** — verify Cancel actually releases it.
- **Windows Firewall** may prompt on first `HttpListener` bind. Test against the
  **Velopack-installed** build, not just the Editor.
- **Restart behaviour is the subtle one.** `PlayerAccountService` keeps its refresh token
  in memory only, so after relaunch `PlayerAccountService.IsSignedIn` is `false` even
  though Unity Authentication restored the session fine. `AccountManager.HasUnityAccount`
  therefore checks three sources (live session → `PlayerInfo.GetUnityId()` → a per-player
  PlayerPrefs flag). **Test this explicitly:** sign in, fully quit, relaunch — Settings
  must still say you have an account and sign-out must NOT show the "lost forever"
  warning. The account email will be blank after a restart (it lives in the in-memory ID
  token); the card falls back to "Managed by your Unity account", which is intended.
- Signing in from a guest session runs `EndGuestSession`, which **deletes that guest's
  local decks and replays**. That is pre-existing, deliberate behaviour (guest profiles are
  throwaway) and the folders don't collide — guests live under `Decks/guest_<id>`, accounts
  under `Decks/<playerId>`. Worth knowing before a playtester builds decks as a guest and
  then signs in; if you'd rather carry them over, that's a separate feature.
