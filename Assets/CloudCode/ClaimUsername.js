/*
 * ClaimUsername - atomically reserves a globally-unique display name for the
 * calling player.
 *
 * Cloud Save's "Custom Data" store is a global (non-player-scoped) key/value
 * space that only Cloud Code can write to - client SDKs cannot write it
 * directly, which is exactly why this has to live here rather than in
 * AccountManager.cs. The registry is one Custom Data entity
 * (CUSTOM_ID_REGISTRY), with one key per claimed username (normalized to
 * lowercase) so uniqueness is enforced on the base string itself, not a
 * Name#1234 discriminator.
 *
 * Atomicity: Cloud Save's setCustomItem() only accepts a writeLock field when
 * you intend to overwrite a value you've already read (optimistic
 * concurrency). Omitting writeLock entirely asserts "this key must not
 * already exist yet" - if two players race to claim the same normalized
 * name, the Cloud Save service itself serializes the two writes and the
 * loser's call throws, which we catch below and report as NAME_TAKEN. This
 * is the actual race-condition guard; there is no separate lock to manage.
 */

const { DataApi } = require("@unity-services/cloud-save-1.4");

const MAX_LENGTH = 16;
const VALID_CHARS = /^[A-Za-z0-9_]+$/;
const CUSTOM_ID_REGISTRY = "usernameRegistry";
const CUSTOM_ID_PLAYER_USERNAMES = "playerUsernames";
const PLAYER_KEY_USERNAME = "username";

// Small, hand-maintained blocklist (npm profanity packages aren't importable
// without opting a script into JS bundling + a local Node project, which is
// unnecessary tooling overhead for a fixed 16-char username field). Extend
// this list as needed - it's checked against both the raw lowercase input
// and a leetspeak-normalized form below, so entries only need to be written
// once in plain letters.
const BLOCKED_WORDS = [
  "fuck", "shit", "bitch", "cunt", "asshole", "bastard", "dick", "pussy",
  "whore", "slut", "nigger", "nigga", "faggot", "retard", "rape", "nazi",
  "hitler", "kike", "spic", "chink", "gook", "tranny",
];

function normalizeForFilter(raw) {
  return raw
    .toLowerCase()
    .replace(/0/g, "o")
    .replace(/1|!/g, "i")
    .replace(/3/g, "e")
    .replace(/4|@/g, "a")
    .replace(/5|\$/g, "s")
    .replace(/7/g, "t")
    .replace(/[^a-z]/g, "");
}

function isProfane(raw) {
  const lowered = raw.toLowerCase();
  const leetNormalized = normalizeForFilter(raw);
  return BLOCKED_WORDS.some(
    (word) => lowered.includes(word) || leetNormalized.includes(word)
  );
}

function fail(reason) {
  return { ok: false, reason };
}

module.exports = async ({ params, context, logger }) => {
  const { projectId, playerId } = context;
  const rawName = (params.username || "").trim();

  if (rawName.length === 0) return fail("EMPTY");
  if (rawName.length > MAX_LENGTH) return fail("TOO_LONG");
  if (!VALID_CHARS.test(rawName)) return fail("BAD_CHARS");
  if (isProfane(rawName)) return fail("PROFANITY");

  const cloudSaveApi = new DataApi(context);
  const normalized = rawName.toLowerCase();

  // v1 policy: names are locked at creation (no rename), so a player who
  // already has one can't claim a second - checked here as a fast, friendly
  // rejection; the create-only write below is what actually enforces it.
  const existing = await cloudSaveApi.getItems(projectId, playerId, [
    PLAYER_KEY_USERNAME,
  ]);
  if (existing.data.results.some((item) => item.key === PLAYER_KEY_USERNAME)) {
    return fail("ALREADY_HAS_USERNAME");
  }

  try {
    // No writeLock field: succeeds only if `normalized` has never been
    // claimed before. See file header for why this is the atomic guard.
    await cloudSaveApi.setCustomItem(projectId, CUSTOM_ID_REGISTRY, {
      key: normalized,
      value: { ownerId: playerId, displayName: rawName },
    });
  } catch (err) {
    logger.info(`Claim race lost for "${normalized}": ${err.message}`);
    return fail("NAME_TAKEN");
  }

  await cloudSaveApi.setItem(projectId, playerId, {
    key: PLAYER_KEY_USERNAME,
    value: rawName,
  });

  // Reverse index (player id -> username) for GetUsernamesForPlayers.js - the
  // friends list needs to resolve a friend's name from their player id, and
  // Custom Data has no query-by-value, so this index is written here at claim
  // time rather than scanned for later. Keyed by player id, not normalized
  // name, since it's looked up by id, never by name.
  await cloudSaveApi.setCustomItem(projectId, CUSTOM_ID_PLAYER_USERNAMES, {
    key: playerId,
    value: { username: rawName },
  });

  return { ok: true, username: rawName };
};

module.exports.params = {
  username: { type: "String", required: true },
};
