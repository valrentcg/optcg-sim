/*
 * RequestPasswordReset - starts the "forgot password" flow.
 *
 * Unity's Username/Password identity provider has no built-in reset-email
 * capability (confirmed against the installed Authentication SDK - there is
 * no SendPasswordResetEmailAsync or equivalent), so this and
 * ConfirmPasswordReset.js implement it ourselves: generate a short-lived
 * token, store it server-side, and email it via a third-party transactional
 * email API (Resend) since Cloud Code can call out via axios but can't send
 * mail itself. Resend was chosen over SendGrid specifically because SendGrid
 * retired its permanent free plan in 2025 (now a 60-day trial only); Resend's
 * free tier (3,000 emails/month, 100/day) is not a trial and comfortably
 * covers password-reset volume for an indie game.
 *
 * Always returns {ok:true} whether or not the email is registered, so a
 * caller can't use this to enumerate which emails have accounts.
 *
 * REQUIRES (Dashboard setup, not scriptable from here):
 *  - A Resend account (resend.com) with a verified sender address/domain.
 *  - The Resend API key stored as a Cloud Code secret named
 *    RESEND_API_KEY (Dashboard > Cloud Code > Secrets, or Secret Manager).
 *  - RESET_SENDER_EMAIL secret set to your verified Resend sender address
 *    (e.g. "One Piece TCG Sim <reset@yourdomain.com>", or Resend's own
 *    onboarding@resend.dev sender while testing, before you verify a domain).
 */

const axios = require("axios-0.21");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const CUSTOM_ID_EMAIL_REGISTRY = "emailRegistry";
const CUSTOM_ID_PASSWORD_RESETS = "passwordResets";
const TOKEN_TTL_MS = 30 * 60 * 1000; // 30 minutes
const TOKEN_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
const TOKEN_LENGTH = 40;

// Node's crypto module isn't available in Unity Cloud Code's JS sandbox
// (confirmed - require("crypto") fails to compile there), so this can't use
// a cryptographically-secure RNG. Math.random() is a weaker source, which is
// why the token is long (40 chars, ~238 bits of nominal entropy) rather than
// a short hex id - combined with the 30-minute TTL and one-time use (deleted
// on redemption in ConfirmPasswordReset.js), this is an acceptable tradeoff
// for a password-reset code, not a long-lived credential.
function generateToken() {
  let token = "";
  for (let i = 0; i < TOKEN_LENGTH; i++) {
    token += TOKEN_CHARS[Math.floor(Math.random() * TOKEN_CHARS.length)];
  }
  return token;
}

module.exports = async ({ params, context, logger, secretManager }) => {
  const { projectId } = context;
  const email = (params.email || "").trim().toLowerCase();

  if (email.length === 0) {
    return { ok: true }; // still don't reveal anything about validation
  }

  const cloudSaveApi = new DataApi(context);

  const lookup = await cloudSaveApi.getCustomItems(projectId, CUSTOM_ID_EMAIL_REGISTRY, [
    email,
  ]);
  const entry = lookup.data.results.find((item) => item.key === email);

  // No account for this email - report success anyway, send nothing.
  if (!entry) {
    logger.info(`Password reset requested for unregistered email ${email}`);
    return { ok: true };
  }

  const token = generateToken();
  await cloudSaveApi.setCustomItem(projectId, CUSTOM_ID_PASSWORD_RESETS, {
    key: token,
    value: {
      playerId: entry.value.playerId,
      email,
      expiresAt: Date.now() + TOKEN_TTL_MS,
    },
  });

  let apiKey;
  try {
    const secret = await secretManager.getSecret("RESEND_API_KEY");
    apiKey = secret.value;
  } catch (err) {
    logger.error("Failed to retrieve RESEND_API_KEY secret", { "error.message": err.message });
    throw err;
  }

  let senderEmail;
  try {
    const senderSecret = await secretManager.getSecret("RESET_SENDER_EMAIL");
    senderEmail = senderSecret.value;
  } catch (err) {
    logger.error("Failed to retrieve RESET_SENDER_EMAIL secret", { "error.message": err.message });
    throw err;
  }

  try {
    await axios.post(
      "https://api.resend.com/emails",
      {
        from: senderEmail,
        to: [email],
        subject: "Reset your password",
        text:
          `Use this code in-app to reset your password: ${token}\n\n` +
          `This code expires in 30 minutes. If you didn't request this, ignore this email.`,
      },
      { headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" } }
    );
  } catch (err) {
    logger.error("Failed to send reset email via Resend", { "error.message": err.message });
    throw err;
  }

  return { ok: true };
};

module.exports.params = {
  email: { type: "String", required: true },
};
