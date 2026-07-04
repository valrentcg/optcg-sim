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

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const email = (params.email || "").trim().toLowerCase();
  const username = (params.username || "").trim();

  if (email.length === 0) {
    return { ok: false, reason: "EMPTY" };
  }

  const cloudSaveApi = new DataApi(context);
  // username included so GetLoginNameForEmail.js can support signing in by
  // email (email -> login username -> Unity Auth).
  await cloudSaveApi.setCustomItem(projectId, CUSTOM_ID_EMAIL_REGISTRY, {
    key: email,
    value: { playerId, username },
  });

  return { ok: true };
};

module.exports.params = {
  email: { type: "String", required: true },
  username: { type: "String", required: true },
};
