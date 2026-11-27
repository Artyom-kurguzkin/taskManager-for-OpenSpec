// Runs in <head>, before the first frame and before Blazor boots: the theme the user chose last time (app.js stores it from Settings → Theme),
// so the first paint is not light on a dark window. html[data-theme] always gets a concrete id: "system" resolves to light or dark from
// the OS (again whenever the OS flips), and nothing stored or an unknown value gives light.
// The one list of theme ids on the page; Helpers/Themes.cs holds the same ids for the app.
(function () {
  var known = ["light", "dark", "novita", "ayu-mirage"];
  var os = matchMedia("(prefers-color-scheme: dark)");
  var setting = "light";
  function resolve(s) { return s === "system" ? (os.matches ? "dark" : "light") : known.indexOf(s) >= 0 ? s : "light"; }
  function apply(s) { setting = s; document.documentElement.setAttribute("data-theme", resolve(s)); }
  os.addEventListener("change", function () { if (setting === "system") apply(setting); });
  window.rtmTheme = { known: known, resolve: resolve, apply: apply };   // app.js setTheme
  var stored = null;
  try { stored = localStorage.getItem("rtm.theme"); } catch (e) { /* storage blocked: light */ }
  apply(stored);
})();
