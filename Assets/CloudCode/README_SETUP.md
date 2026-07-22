# Account Email / Password-Recovery — Setup & Proofing Checklist

The client (`AccountManager.cs`) and UI (`MainMenuManager.cs`) are complete and verified.
Everything that makes this feature "not work at all" is **server-side deployment/config** plus
two code bugs that are now fixed (see bottom). This doc is the exact remaining setup + how to
verify end-to-end.

## The flow

| Client method | Cloud Code script | Sends email? |
|---|---|---|
| `LinkEmailPasswordAsync` (register) | `RegisterEmailForRecovery` + `SendWelcomeEmail` | ✅ welcome |
| `SignInWithEmailPasswordAsync` | `GetLoginNameForEmail` (email→username), self-heals registry | — |
| `RequestPasswordResetAsync` | `RequestPasswordReset` (emails a 40-char code, 30-min TTL) | ✅ reset code |
| `ConfirmPasswordResetAsync` | `ConfirmPasswordReset` (verifies code, force-sets password via admin API) | — |

Emails go out via **Resend** (`api.resend.com/emails`) from inside the Cloud Code scripts.

## Required setup (none of this is scriptable — must be done in the dashboards)

### 1. Deploy the Cloud Code scripts
Deploy ALL of these to UGS Cloud Code (Editor → **Deployment** window, select the `.js` files,
Deploy — or the UGS CLI). The endpoint name must equal the file name:
`RegisterEmailForRecovery`, `SendWelcomeEmail`, `GetLoginNameForEmail`, `RequestPasswordReset`,
`ConfirmPasswordReset`, `ClaimUsername`, `LookupPlayerByUsername`, `GetUsernamesForPlayers`.
> If these were never deployed, that alone = "doesn't work at all." Confirm each appears in
> Dashboard → Cloud Code → Scripts.

### 2. Enable the identity provider
Dashboard → Authentication → **Username/Password** provider = enabled.

### 3. Resend account
- Create a Resend account (resend.com), free tier (3,000/mo, 100/day) is enough.
- Verify a sender **domain** (or, for testing only, use Resend's `onboarding@resend.dev` sender
  and send to your own inbox — unverified domains can only mail the account owner).

### 4. Cloud Code secrets (Dashboard → Cloud Code → Secrets)
| Secret | Value |
|---|---|
| `RESEND_API_KEY` | Resend API key (`re_...`) |
| `RESET_SENDER_EMAIL` | verified sender, e.g. `One Piece TCG Sim <noreply@yourdomain>` (or `onboarding@resend.dev` while testing) |
| `UNITY_SA_BASIC` | **base64(`<KEY_ID>:<SECRET_KEY>`)** of a Service Account (see 5) — pre-encode it; the JS sandbox can't base64 in-script |

### 5. Service Account for the admin password-set
Dashboard → Administration → **Service Accounts** → create one, create a key (KEY_ID + SECRET),
and give it a **role with Player Authentication admin access**. Then set `UNITY_SA_BASIC` =
base64 of `KEY_ID:SECRET`.

### 6. ⚠️ Confirm the admin endpoint in `ConfirmPasswordReset.js`
The base path `/player-auth-admin/v1/` is confirmed, and auth is now correct (Service Account
Basic). The exact **resource sub-path + body** (`.../projects/{projectId}/players/{playerId}/username-password`,
body `{ password }`) is a best-effort guess — verify against the rendered
`services.docs.unity.com/player-auth-admin/v1` reference and adjust `ADMIN_API_PASSWORD_PATH` /
the PUT body if they differ. On failure the script now logs the HTTP status + response body, so
the Cloud Code logs will show the exact mismatch.

## Verify end-to-end (the actual proof)
1. **Welcome email:** register a new account with a real email → a "Welcome aboard" email arrives.
2. **Reset request:** Sign out → Sign in screen → Forgot password → enter that email → a reset
   code email arrives (check Cloud Code logs if not — likely a missing secret or unverified sender).
3. **Reset confirm:** enter the code + a new password in-app → returns success → sign in with the
   NEW password works. (If this step fails but the email arrived, it's the step-6 admin endpoint.)
4. Check Dashboard → Cloud Code → Logs for any `logger.error` from the scripts.

## Bugs fixed in code (2026-07-22, headless — need the above to verify live)
1. **`ConfirmPasswordReset.js.meta` was missing the `newPassword` parameter** — the deployed
   endpoint would drop the new password → every reset failed with `MISSING_FIELDS`. Added it.
   *(Re-deploy ConfirmPasswordReset after this change.)*
2. **`ConfirmPasswordReset.js` authenticated the admin call with `Bearer context.serviceToken`** —
   admin APIs reject that; they require Service Account HTTP Basic. Switched to `Basic UNITY_SA_BASIC`
   and added rich error logging.

Everything else (client contracts, registry hex-keys, writeLock handling, token TTL/one-time-use,
email-enumeration protection, welcome + reset-send Resend calls) was audited and is correct.
