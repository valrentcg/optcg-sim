/*
 * SendWelcomeEmail - one-shot "welcome aboard" email fired right after a player
 * finishes registration (name claimed + email/password linked).
 *
 * Same delivery rails as RequestPasswordReset.js: Resend's transactional API
 * via axios, authenticated with the RESEND_API_KEY secret and sent from the
 * RESET_SENDER_EMAIL address. If those two secrets are configured for the
 * reset flow, this script needs zero extra setup.
 *
 * Fire-and-forget from the client's point of view: AccountManager calls it
 * after a successful link and ignores failures - a missing welcome email must
 * never make registration look broken. Errors here still return ok:false so
 * they're visible in Cloud Code logs while tuning.
 */

const axios = require("axios-0.21");

module.exports = async ({ params, logger, secretManager }) => {
  const email = (params.email || "").trim();
  const username = (params.username || "Captain").trim();

  if (email.length === 0) return { ok: false, reason: "EMPTY" };

  let apiKey, senderEmail;
  try {
    apiKey = (await secretManager.getSecret("RESEND_API_KEY")).value;
    senderEmail = (await secretManager.getSecret("RESET_SENDER_EMAIL")).value;
  } catch (err) {
    logger.error("Welcome email secrets missing", { "error.message": err.message });
    return { ok: false, reason: "NO_SECRETS" };
  }

  const subject = `Welcome aboard, ${username}!`;
  const text =
    `Ahoy, ${username}!\n\n` +
    `Your crew is assembled and your name is on the registry - it's time to ` +
    `set sail on a grand adventure.\n\n` +
    `Here's what awaits you on the high seas:\n` +
    `  - Build decks around your favorite leaders\n` +
    `  - Battle other captains in private lobbies\n` +
    `  - Track every voyage in your match history\n` +
    `  - Add friends and grow your crew\n\n` +
    `Your account is protected: sign in any time with your name (${username}) ` +
    `or this email address, on any device.\n\n` +
    `May the winds favor you,\n` +
    `The One Piece TCG Simulator crew\n\n` +
    `P.S. You're receiving this because an account was just created with this ` +
    `address. If that wasn't you, you can safely ignore this email.`;

  try {
    await axios.post(
      "https://api.resend.com/emails",
      { from: senderEmail, to: [email], subject, text },
      { headers: { Authorization: `Bearer ${apiKey}`, "Content-Type": "application/json" } }
    );
  } catch (err) {
    logger.error("Failed to send welcome email via Resend", { "error.message": err.message });
    return { ok: false, reason: "SEND_FAILED" };
  }

  return { ok: true };
};

module.exports.params = {
  email: { type: "String", required: true },
  username: { type: "String", required: true },
};
