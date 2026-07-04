/*
 * GetUsernamesForPlayers - batch reverse lookup, player ids -> claimed
 * usernames.
 *
 * The friends list only has player ids from the Friends service (relationship
 * members are identified by id, not by our username system, and the Friends
 * service's own Profile.Name is tied to Unity's separate discriminator-based
 * Player Name service, not our globally-unique names - see FriendsManager.cs
 * for why that field is deliberately never read). This resolves those ids
 * back to real usernames via the playerUsernames index ClaimUsername.js
 * writes at claim time. Batched into one call rather than one script call per
 * friend, since a friends list can be dozens of rows.
 */

const { DataApi } = require("@unity-services/cloud-save-1.4");

const CUSTOM_ID_PLAYER_USERNAMES = "playerUsernames";
const MAX_BATCH = 100;

module.exports = async ({ params, context, logger }) => {
  const { projectId } = context;
  const playerIds = Array.isArray(params.playerIds) ? params.playerIds.slice(0, MAX_BATCH) : [];

  if (playerIds.length === 0) {
    return { ok: true, usernames: {} };
  }

  const cloudSaveApi = new DataApi(context);
  const result = await cloudSaveApi.getCustomItems(projectId, CUSTOM_ID_PLAYER_USERNAMES, playerIds);

  const usernames = {};
  for (const item of result.data.results) {
    usernames[item.key] = item.value.username;
  }

  return { ok: true, usernames };
};

module.exports.params = {
  playerIds: { type: "JSON", required: true },
};
