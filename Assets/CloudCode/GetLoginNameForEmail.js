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

module.exports = async ({ params, context }) => {
  const { projectId } = context;
  const email = (params.email || "").trim().toLowerCase();

  if (email.length === 0) return { ok: false, reason: "EMPTY" };

  const cloudSaveApi = new DataApi(context);
  const lookup = await cloudSaveApi.getCustomItems(projectId, CUSTOM_ID_EMAIL_REGISTRY, [
    email,
  ]);
  const entry = lookup.data.results.find((item) => item.key === email);

  if (!entry || !entry.value.username) return { ok: false, reason: "NOT_FOUND" };

  return { ok: true, username: entry.value.username };
};

module.exports.params = {
  email: { type: "String", required: true },
};
