/*
 * GetLoginNameForEmail - resolves a registered recovery email to the account's
 * login username, so the sign-in screen can accept "username or email" while
 * Unity Authentication itself only understands usernames.
 *
 * Reads the same emailRegistry entity RegisterEmailForRecovery.js writes.
 * Exposure consideration: typing someone's email reveals their public game
 * name - acceptable, since usernames are public identity anyway. The reverse
 * (username -> email) is never exposed.
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


module.exports = async ({ params, context }) => {
  const { projectId } = context;
  const email = (params.email || "").trim().toLowerCase();

  if (email.length === 0) return { ok: false, reason: "EMPTY" };

  const cloudSaveApi = new DataApi(context);
  let entry;
  try {
    const lookup = await cloudSaveApi.getCustomItems(projectId, CUSTOM_ID_EMAIL_REGISTRY, [
      emailKey(email),
    ]);
    entry = lookup.data.results.find((item) => item.key === emailKey(email));
    if (!entry) {
      // Diagnostic detail rides in `reason` so the client console shows exactly
      // what happened without needing dashboard log access.
      return {
        ok: false,
        reason: `MISS key=${emailKey(email)} got=${lookup.data.results.length}`,
      };
    }
  } catch (err) {
    return { ok: false, reason: `ERR ${String(err.message).substring(0, 120)}` };
  }

  if (!entry.value.username) return { ok: false, reason: "NO_USERNAME_IN_ENTRY" };

  return { ok: true, username: entry.value.username };
};

module.exports.params = {
  email: { type: "String", required: true },
};
