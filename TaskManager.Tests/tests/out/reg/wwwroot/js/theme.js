// Runs in <head>, before the first frame and before Blazor boots: the theme the user chose last time (app.js stores it from Settings → Theme),
// so the first paint is not light on a dark window. Nothing stored yet: index.html's own data-theme (light, the default) stands.
(function () {
  try {
    var theme = localStorage.getItem("rtm.theme"), root = document.documentElement;
    if (theme === "dark" || theme === "light") root.setAttribute("data-theme", theme);
    else if (theme === "system") root.removeAttribute("data-theme");   // the OS decides (app.css)
  } catch (e) { /* storage blocked: the default stands */ }
})();
