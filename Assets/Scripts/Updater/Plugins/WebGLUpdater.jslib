// WebGL-only helpers for the update system.
// Placed under a Plugins folder so Unity links it into the WebGL build.
mergeInto(LibraryManager.library, {

  // Hard-reload the page, bypassing HTTP cache for index.html by adding a
  // cache-busting query param. Hashed build files then resolve to the new
  // filenames referenced by the fresh index.html.
  OPTCG_ReloadPage: function () {
    var url = new URL(window.location.href);
    url.searchParams.set("v", Date.now().toString());
    window.location.replace(url.toString());
  },

  // Open a URL in a new tab (used for "download the desktop client" links).
  OPTCG_OpenURL: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    window.open(url, "_blank");
  }
});
