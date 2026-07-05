/*
 * RegisterEmailForRecovery - records a lowercased email -> playerId mapping
 * so a later "forgot password" request can find which account an email
 * belongs to.
 *
 * This is NOT what enforces "one account per email" - Unity's own
 * AddUsernamePasswordAsync already rejects linking an email that's already
 * in use elsewhere, so this index only needs to be good enough for reverse
 * lookup during password reset, not uniqueness. Called by AccountManager
 * immediately after AddUsernamePasswordAsync succeeds, with the caller's own
 * Cloud Code context.playerId as the value - the player can only register
 * their own current identity, never someone else's.
 */

const { DataApi } = require("@unity-services/cloud-save-1.4");

const CUSTOM_ID_EMAIL_REGISTRY = "emailRegistry";

// Cloud Save keys only allow [A-Za-z0-9-_], so a raw email ("a@b.com") is an
// ILLEGAL key and every write/lookup using one is rejected on validation.
// Encode the (lowercased) email to hex for use as the registry key; the plain
// address is kept in the value for debugging. All scripts touching
// emailRegistry (RegisterEmailForRecovery / GetLoginNameForEmail /
// RequestPasswordReset) must use this same derivation.
function emailKey(email) {
  let hex = "";
  for (let i = 0; i < email.length; i++) {
    hex += email.charCodeAt(i).toString(16).padStart(2, "0");
  }
  return "e_" + hex;
}


module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const email = (params.email || "").trim().toLowerCase();
  const username = (params.username || "").trim();

  if (email.length === 0) {
    return { ok: false, reason: "EMPTY" };
  }

  const cloudSaveApi = new DataApi(context);

  // THE BUG THIS FIXES: setCustomItem WITHOUT a writeLock is create-only -
  // it throws a conflict if the key already exists (the exact mechanism
  // ClaimUsername.js exploits for uniqueness). Registering an email a second
  // time - re-linking, self-heal on sign-in, recovery email updates - was
  // therefore guaranteed to fail. Updates must echo the existing writeLock.
  try {
    // The lookup itself can 404 when the emailRegistry entity has never been
    // written (fresh project / after a data wipe) - that just means "no
    // existing entry", not a failure. Only the WRITE below is fatal.
    let existing;
    try {
      const lookup = await cloudSaveApi.getCustomItems(projectId, CUSTOM_ID_EMAIL_REGISTRY, [
        emailKey(email),
      ]);
      existing = lookup.data.results.find((item) => item.key === emailKey(email));
    } catch (lookupErr) {
      logger.info(`emailRegistry lookup failed (treating as empty): ${lookupErr.message}`);
      existing = undefined;
    }

    // An email already bound to a DIFFERENT account is not overwritable -
    // otherwise anyone could hijack the email -> account mapping that
    // password reset and email sign-in resolve through.
    if (existing && existing.value.playerId && existing.value.playerId !== playerId) {
      return { ok: false, reason: "EMAIL_TAKEN" };
    }

    const body = { key: emailKey(email), value: { playerId, username, email } };
    if (existing) body.writeLock = existing.writeLock;
    await cloudSaveApi.setCustomItem(projectId, CUSTOM_ID_EMAIL_REGISTRY, body);
  } catch (err) {
    logger.error("emailRegistry write failed", {
      "error.message": err.message,
      "error.status": err.response ? String(err.response.status) : "none",
      "error.data": err.response ? JSON.stringify(err.response.data) : "none",
    });
    return { ok: false, reason: "WRITE_FAILED" };
  }

  return { ok: true };
};

module.exports.params = {
  email: { type: "String", required: true },
  username: { type: "String", required: true },
};
