// Puts the download for the visitor's system first and shows the latest version from GitHub.
// No cookies, no tracking: the only request is GitHub's public "latest release" API.
(function () {
  "use strict";

  var ua = navigator.userAgent || "";
  var platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
  var isMac = /mac/i.test(platform) || /Macintosh/.test(ua);
  var isWindows = /win/i.test(platform) || /Windows/.test(ua);

  // The hero button links to the visitor's download; the other system is offered next to it.
  var primary = document.getElementById("download-primary");
  var secondary = document.getElementById("download-secondary");
  if (primary && secondary && isMac && !isWindows) {
    var swap = primary.innerHTML;
    var swapHref = primary.getAttribute("href");
    primary.innerHTML = secondary.innerHTML;
    primary.setAttribute("href", secondary.getAttribute("href"));
    secondary.innerHTML = swap;
    secondary.setAttribute("href", swapHref);
  }

  document.querySelectorAll("[data-os]").forEach(function (card) {
    var os = card.getAttribute("data-os");
    if ((os === "mac" && isMac && !isWindows) || (os === "windows" && (isWindows || !isMac))) {
      card.classList.add("recommended");
    }
  });

  var versionTargets = document.querySelectorAll("[data-version]");
  if (versionTargets.length === 0 || !window.fetch) {
    return;
  }

  fetch("https://api.github.com/repos/eneskaramanoglu/lingocue/releases/latest", { headers: { Accept: "application/vnd.github+json" } })
    .then(function (response) { return response.ok ? response.json() : null; })
    .then(function (release) {
      if (!release || typeof release.tag_name !== "string" || !/^v?\d+\.\d+\.\d+$/.test(release.tag_name)) {
        return;
      }

      var version = release.tag_name.replace(/^v/, "");
      versionTargets.forEach(function (el) { el.textContent = version; });
    })
    .catch(function () { /* keep the version written in the page */ });
})();
