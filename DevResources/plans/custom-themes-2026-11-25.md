# Custom themes: Novita and Ayu Mirage beside light and dark (#wi225)

Status: done 2026-11-25 — chunks A and C staged; checked in the hidden app in all four themes; no test runs (owner); In Review

Research: `DevResources/plans/custom-themes-research-2026-11-25.md` (34 sources). Audit: summarised below (the stylesheet is about 90 percent ready).

## Why

The owner wants custom theming, not just day/night: the stylesheet arranged for it, two more themes (Novita, the look of `Documents\1-novita\Business Intelligence And Finance Tools`; Ayu Mirage, the owner's VS Code theme "Ayu Mirage Bordered"), and the chosen theme kept as a user setting.

## What others do (from the research)

- Palette separate from roles, each surface paired with an "on" colour (Primer, Radix, Material, shadcn, daisyUI, Catppuccin).
- One `:root[data-theme="id"]` block per theme with its own `color-scheme`; `light-dark()` only handles two schemes, so it is not used.
- "System" is a setting resolved to a concrete theme in a synchronous head script, re-resolved on the OS change event.
- Hover, selected and soft tones derived with `color-mix(in oklab, …)`; a theme sets a value itself where the derived one fails contrast.

## Decisions

- Theme ids: `light`, `dark`, `novita`, `ayu-mirage`; the setting may also be `system` (resolves to light or dark). `data-theme` on `<html>` always holds a concrete id, never `system`. An unknown saved id falls back to light.
- The existing token names stay (paper, card, ink, ink-2, line, line-soft, shadow, accent, the 12 pastels, stone, on-fill, ok): a rename across the stylesheet and the C# colour tables buys nothing. Added: `--on-accent`, `--hover` (split from `--stone`), `--selection`, `--shadow-soft`; `--alert` / `--on-alert` move into the token layer. In light and dark every existing token keeps its value, so the two current looks do not change.
- The dark palette is written once; the `@media (prefers-color-scheme)` block and the `:root:not([data-theme="light"])` selector go (they would also match the new themes).
- Ayu Mirage: values from the installed extension (teabyii.ayu 1.6.0, MIT); muted text, on-accent and borders lifted to reach WCAG AA (research section 8).
- Novita: colours only; the app keeps its own font and shape. Values from the Novita look agent (`scratchpad/novita-theme.md`), else the audit's table.
- The native title bar follows the theme: its colours live with the theme list in code, not as a second light/dark copy.
- Settings: the Theme row lists System, Light, Dark, Novita, Ayu Mirage; the choice is the existing `Theme` setting (per user, `%APPDATA%\RepoTaskManager\settings.json`).

## Chunks

| Chunk | Owns | Tests |
|---|---|---|
| A stylesheet | `TaskManager/wwwroot/css/app.css`; the palette test in `ArrangingTheBoardTests`; new `ThemingTheStylesheetTests` | every theme sets every token; contrast pairs per theme; no colour literal outside the token layer |
| C setting, start-up, window frame | `Helpers/Themes.cs` (new), `Helpers/WindowFrame.cs`, `SettingsPage.razor`, `MainLayout.razor` (ApplyTheme, watchDark), `wwwroot/js/theme.js`, `app.js` (setTheme, watchDark), `index.html`, `tests/hidden-app.ps1` (Set-Theme), `SettingsStore.cs` only if needed; new `ChoosingAThemeTests`; the existing theme tests | choosing a theme saves and applies it; system resolves; unknown falls back; the title bar takes the theme's colours |

A and C meet only at the theme ids and the `data-theme` attribute.

## Verification

Hidden desktop, CDP, 1600px: board, a work item, Settings and the split view in each of the four themes; screenshots; the Settings choice survives a restart.
