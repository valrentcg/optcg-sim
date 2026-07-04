/*
 * ConfirmPasswordReset - second half of the "forgot password" flow started
 * by RequestPasswordReset.js. Verifies the emailed token and, if valid,
 * force-sets the account's password.
 *
 * ====================== NEEDS ONE THING CONFIRMED ======================
 * Changing a password you don't already know requires Unity's Player
 * Authentication ADMIN API (services.docs.unity.com/player-auth-admin/v1) -
 * this is a separate, Swagger/JS-rendered doc page that couldn't be read
 * programmatically while writing this script, so the exact endpoint path
 * below (ADMIN_API_PASSWORD_PATH) is a best-effort placeholder, NOT a
 * confirmed value. Before deploying: open that admin API reference in a
 * browser, find the operation that sets a player's Username/Password
 * credential, and replace ADMIN_API_PASSWORD_PATH + the request body shape
 * to match exactly. The function throws clearly if this hasn't been done.
 * =========================================================================
 *
 * Auth for that call: Unity's docs describe admin endpoints as requiring
 * Service Account credentials, separate from a normal player accessToken.
 * Cloud Code's own context.serviceToken is tried first below since it's
 * already available with zero extra setup; if the Admin API rejects it,
 * provision a dedicated Service Account key (Dashboard > Service Accounts)
 * scoped for Authentication admin access, store it as a Cloud Code secret
 * (e.g. AUTH_ADMIN_SERVICE_TOKEN), and swap it in below instead.
 */

const axios = require("axios-0.21");
const { DataApi } = require("@unity-services/cloud-save-1.4");

const CUSTOM_ID_PASSWORD_RESETS = "passwordResets";
const ADMIN_API_BASE_URL = "https://services.api.unity.com";
// PLACEHOLDER - confirm against services.docs.unity.com/player-auth-admin/v1
const ADMIN_API_PASSWORD_PATH = (projectId, playerId) =>
  `/player-auth-admin/v1/projects/${projectId}/players/${playerId}/username-password`;

module.exports = async ({ params, context, logger, secretManager }) => {
  const { projectId, serviceToken } = context;
  const token = (params.token || "").trim();
  const newPassword = params.newPassword || "";

  if (token.length === 0 || newPassword.length === 0) {
    return { ok: false, reason: "MISSING_FIELDS" };
  }

  const cloudSaveApi = new DataApi(context);
  const lookup = await cloudSaveApi.getCustomItems(projectId, CUSTOM_ID_PASSWORD_RESETS, [
    token,
  ]);
  const entry = lookup.data.results.find((item) => item.key === token);

  if (!entry) {
    return { ok: false, reason: "INVALID_TOKEN" };
  }
  if (Date.now() > entry.value.expiresAt) {
    return { ok: false, reason: "EXPIRED" };
  }

  let adminToken = serviceToken;
  try {
    const override = await secretManager.getSecret("AUTH_ADMIN_SERVICE_TOKEN");
    if (override && override.value) adminToken = override.value;
  } catch (err) {
    // Secret not configured - fine, fall back to context.serviceToken.
  }

  try {
    await axios.put(
      ADMIN_API_BASE_URL + ADMIN_API_PASSWORD_PATH(projectId, entry.value.playerId),
      { password: newPassword },
      { headers: { Authorization: `Bearer ${adminToken}`, "Content-Type": "application/json" } }
    );
  } catch (err) {
    logger.error("Admin password update failed - is ADMIN_API_PASSWORD_PATH confirmed yet?", {
      "error.message": err.message,
    });
    throw err;
  }

  // One-time use: remove the token regardless of what comes next.
  await cloudSaveApi.deleteCustomItem(projectId, CUSTOM_ID_PASSWORD_RESETS, token);

  return { ok: true };
};

module.exports.params = {
  token: { type: "String", required: true },
  newPassword: { type: "String", required: true },
};
