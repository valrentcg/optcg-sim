/*
 * LookupPlayerByUsername - read-only resolution of a normalized username to
 * its owning player id.
 *
 * Custom Data isn't client-readable any more than it's client-writable, so
 * this has to be Cloud Code too. Not consumed by anything in this task -
 * it's exposed now as the exact primitive a future "add friend by username"
 * flow needs, so that work doesn't have to touch the registry's storage
 * shape at all.
 */

const { DataApi } = require("@unity-services/cloud-save-1.4");

const CUSTOM_ID_REGISTRY = "usernameRegistry";

module.exports = async ({ params, context, logger }) => {
  const { projectId } = context;
  const normalized = (params.username || "").trim().toLowerCase();

  if (normalized.length === 0) {
    return { ok: false, reason: "EMPTY" };
  }

  const cloudSaveApi = new DataApi(context);
  const result = await cloudSaveApi.getCustomItems(projectId, CUSTOM_ID_REGISTRY, [
    normalized,
  ]);

  const entry = result.data.results.find((item) => item.key === normalized);
  if (!entry) {
    return { ok: false, reason: "NOT_FOUND" };
  }

  return { ok: true, ownerId: entry.value.ownerId, displayName: entry.value.displayName };
};

module.exports.params = {
  username: { type: "String", required: true },
};
