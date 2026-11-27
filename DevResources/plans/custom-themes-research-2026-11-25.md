# Custom themes: research (work item #225)

Date: 2026-11-25. Research only; no code changed. Companion of `custom-themes-2026-11-25.md` (the plan) and the audit of the app's own stylesheet (done by another agent).

Scope: how a plain-CSS desktop app (Blazor in WebView2, one stylesheet, Radzen components) arranges many themes; the Ayu Mirage palette; the mechanics; a recommended token set.

How facts were checked: raw files and package contents were read directly (GitHub raw, unpkg, the GitHub API for commit dates, `mdn/browser-compat-data` for browser versions). Contrast ratios in section 8 were computed by me with the WCAG 2.2 relative-luminance formula; colour mixes with `oklab` maths equal to CSS `color-mix(in oklab, ...)`. Anything not read or only summarised by a fetch tool is marked **unverified**.

Facts about this app come from a quick read of `TaskManager/wwwroot/css/app.css`, `wwwroot/js/theme.js` and the Novita/Ayu change folder only, to size the recommendation. The audit agent owns the real inventory. Novita's colours are in the owner's "finance-tools" project, which is not under `Documents/5-mine`; this file has no Novita values.

---

## 1. Summary of findings

1. Every maintained system separates **primitives** (a palette: hue scales or named colours) from **roles** (what a colour is for: surface, text, border, accent, status). Components read roles only; a theme is a block that sets roles. Sizes vary from 24 roles (Material Web colour tokens) to about 960 declarations per theme file (GitHub Primer, which includes component and terminal tokens). The sweet spot for an app without a design-system team is **20 hand-set roles + a categorical set + a few derived tones**.
2. A pairing convention keeps contrast honest: every surface role has an "on" (content) role (Material `on-*`, shadcn `*-foreground`, daisyUI `*-content`, Radzen `--rz-on-*`).
3. In plain CSS, one `:root[data-theme="id"] { color-scheme: dark; --role: value; ... }` block per theme is the common, simplest mechanism (daisyUI CDN, Open Props switches, Primer, Next.js docs). `light-dark()` only helps when there are exactly two schemes; it does not scale to named themes.
4. `color-mix(in oklab, ...)` and relative colour syntax (`oklch(from var(--x) calc(l + .08) c h)`) are supported in WebView2 (all Chromium 123+; see section 3), so hover, soft tints and muted tones can be derived instead of hand-written in every theme.
5. "System" is a **setting**, not a CSS state: resolve it to a concrete theme id in script before first paint (the app already does that in `theme.js`) and re-resolve on the `prefers-color-scheme` change event. That removes the duplicated `@media (prefers-color-scheme: dark)` block that the current stylesheet repeats beside `[data-theme="dark"]`.
6. A theme author must set values by hand where derivation cannot guarantee contrast or identity: the base surfaces, text, the accent and its on-colour, the status hues, the categorical chips. Verify with WCAG 2.2 ratios (4.5:1 text, 3:1 UI parts) in a test, not by eye.
7. Radzen is themed entirely by `--rz-*` custom properties on `:root`, and Radzen ships its own variants (the `*-wcag` files) as small override files loaded after the base. A host stylesheet can override them, with two traps: Radzen's CSS is **unlayered** (so an override inside an `@layer` loses), and its `-light/-dark/-lighter/-darker` tones are **precomputed by Sass**, so overriding `--rz-primary` alone leaves those stale.
8. Ayu Mirage (official, current): UI background `#1F2430`, editor background `#242936`, text `#CCCAC2` (a translucent `#FEFAEC` at 77%), accent `#FFCC66`, error `#FF6666`. The official UI text colour `#707A8C` and the official on-accent colour `#735923` both **fail** AA in an app (3.5:1 and 4.42:1); the mapping below corrects both.

---

## 2. Token structure for many themes

### 2.1 What real systems expose

| System | Primitive layer | Role layer | Size (verified unless marked) | Pairing / convention |
|---|---|---|---|---|
| GitHub Primer (`@primer/primitives` 11.10.0) | base colour scales | `fgColor-*`, `bgColor-*`, `borderColor-*`, shadows, plus component tokens (`button-primary-bgColor-rest`) and data-vis | `dark.css` declares **959** custom properties (counted); the docs page lists roughly 240 foundational + pattern tokens (tool estimate, **unverified**) | name = `property-role-state`; status roles accent, attention, danger, done, severe, success, sponsors; one file per theme: light, dark, dark-dimmed, colourblind, tritanopia, high-contrast |
| Radix Colors / Themes | 12-step scale per hue | steps are the roles: 1-2 backgrounds, 3-5 component bg (rest/hover/active), 6-8 borders, 9-10 solid, 11-12 text | 12 steps x ~30 hues, light and dark each | step 11 guaranteed Lc 60 and step 12 Lc 90 (APCA) on a step-2 background of the same scale |
| Material 3 (material-web `color.md`) | tonal palettes from 5 key colours | `--md-sys-color-<role>`: primary, secondary, tertiary, error (each with `-container`), surface + 5 surface-container steps + dim/bright, outline, outline-variant, background | 24 tokens listed on the page (counted); every token has a matching `on-` | "All tokens have a corresponding `on-<token>` for content colour with accessible contrast" |
| shadcn/ui | none (oklch literals) | background/foreground, card, popover, primary, secondary, muted, accent (each with `-foreground`), destructive, border, input, ring, chart-1..5, sidebar-* | ~19 core + 8 sidebar + 5 chart + radius | `:root` and `.dark` blocks; surface token drops the word "background" |
| daisyUI 5 | none | base-100/200/300 + base-content; primary, secondary, accent, neutral, info, success, warning, error (each `-content`); radius, size, border, depth, noise | **20 colour variables** (3 base + base-content + 8 semantic + 8 `-content`) plus ~8 shape/effect | theme = one block with `name`, `default`, `prefersdark`, `color-scheme`; selected with `data-theme="name"` |
| Catppuccin | 26 named colours per flavour (14 accents + 12 neutrals: text, subtext1/0, overlay2/1/0, surface2/1/0, base, mantle, crust) | style guide maps functions: base = background pane, mantle/crust = secondary panes, surface0-2 = surface elements, overlay0-2 = overlays, text, subtext = labels, overlay1 = subtle, base = "on accent"; blue = links, green/yellow/red = success/warning/error | 26 (palette v1.8.0 `palette.json`, counted) | guide says "legibility always comes first"; 4 flavours = 4 themes |
| Open Props | 19 hue families x 13 steps | `--surface-1..4`, `--text-1/2`, `--brand` per scheme, aliased to theme-agnostic names | 500+ props total | switch files apply the same values to `.dark`, `[data-theme=dark]` or `prefers-color-scheme` |
| Tailwind v4 | `--color-*` in `@theme` | the same variables; override per theme by redefining them | variables must be top level (not nested under selectors) | `@theme inline` when a theme variable references another variable |
| VS Code (workbench colours) | none | ~hundreds of ids (`editor.background`, `sideBar.background`, `focusBorder`, `badge.*`, `gitDecoration.*`...) | page lists hundreds (exact count **unverified**) | theme file declares `"type": "dark"` |
| Radzen Blazor | `--rz-base-50..900`, base/primary/secondary/info/success/warning/danger each with `-light -lighter -dark -darker`, `--rz-series-1..24` | `--rz-body-background-color`, `--rz-base-background-color`, `--rz-text-color` (+ title/secondary/tertiary/disabled), `--rz-link-color`, `--rz-on-<hue>` | the generator references ~1,300 `--rz-` names (grep count of the SCSS, rough) | free themes: material, standard, default, humanistic, software, each with `-dark` and a `-wcag` override file |

### 2.2 What this means for a small app

- **Primitives are optional in plain CSS.** Primer, Radix and Material need generated scales; a 4-theme app does not. daisyUI, shadcn and Catppuccin themes are hand-set role values, with no primitive layer in the theme file. Keep the palette in the theme block itself (the role values are the palette).
- **Pairing beats volume.** One `on-accent`, one `on-status` and one `on-fill` carry most of the contrast guarantee.
- **Status colours need four hues**: danger, warning, success, info (Primer, daisyUI, Radzen all expose these four; Catppuccin maps three plus teal for info).
- **Categorical colours are a separate family.** shadcn has `chart-1..5`, Radzen `series-1..24`, Primer `data-*`. The app's 13 pastel category fills are this family; they always carry one `on-fill` text colour (Radzen and Primer do the same with a content colour per fill).
- **Each theme declares light or dark** (`color-scheme`, VS Code `type`, daisyUI `color-scheme`, Primer's per-theme file). It drives native parts (scrollbars, form controls, canvas, spellcheck).

### 2.3 Minimal role set (summary; the full recommended list is section 9)

Surfaces (5), text (3), lines (3), accent (3), status (4 + 1 content colour), shadow (1), categorical (13 + 1 content colour) = about 20 hand-set roles + 14 chip values; hover, selected, accent-hover, soft tints = about 7 derived.

---

## 3. Mechanics, with browser support

Browser versions below are from `mdn/browser-compat-data` (read 2026-11-25). WebView2 Evergreen follows stable Chromium, so "Chrome N" means "WebView2 runtime with Chromium N or newer". The runtime version on the owner's machine is **unverified**; over the existing CDP session `Browser.getVersion` returns it.

| Feature | Chrome/Edge since | Baseline (MDN) | Use for |
|---|---|---|---|
| `color-scheme` | 81 | widely available since Jan 2022 | declare per theme: native UI follows the theme, not the OS |
| `@layer` | 99 | widely available since Mar 2022 | order the stylesheet; **unlayered always beats layered** (normal declarations) |
| `@property` | 85 | newly available (Jul 2024) | typed `<color>` tokens, optional: lets a theme switch transition |
| `color-mix()` | 111 | widely available (May 2023) | derive hover/soft/border tones; default space `oklab` |
| `oklch()/oklab()` | 111 | widely available | perceptual lightness maths |
| relative colour syntax | 119 for `color()`, `lch()`, `lab()`; 122 for `oklch()`, `oklab()`, `rgb()`; 125 for `hsl()`/`hwb()` | per function | `oklch(from var(--accent) calc(l - .08) c h)`; percentages are not allowed in `calc(l + 20%)`, use numbers |
| `light-dark()` | 123 | widely available since May 2024 | two-scheme pairs only; **requires** `color-scheme` that includes `dark` |
| `contrast-color()` | 147 | not baseline | picks black/white text automatically; too new to rely on for a theme author who needs a specific on-colour |

### 3.1 One block per theme (`data-theme`)

The common pattern (Next.js docs, daisyUI CDN, Open Props switches, Primer):

```css
:root { /* every role, with the light theme's values: the safe default */ }
:root[data-theme="dark"]       { color-scheme: dark; --bg: ...; }
:root[data-theme="ayu-mirage"] { color-scheme: dark; --bg: #1F2430; ... }
```

- The default block holds every role, so a theme that forgets one still renders (it falls back to light), and a test can assert that each theme block sets the whole contract.
- `:root[data-theme]` has specificity (0,2,0), above plain `:root`, so it wins independent of source order and independent of layers it shares.
- Primer's own selector scheme (verified in `dist/css/functional/themes/dark.css`): `[data-color-mode="dark"][data-dark-theme="dark"], [data-color-mode="auto"][data-light-theme="dark"] { ... }`. It combines a mode (light / dark / auto) with the named theme used for each side, which is the model for "System beside named themes" in 4.1.

### 3.2 `light-dark()` and `color-scheme`

- `light-dark(a, b)` returns `a` when the used scheme is light, else `b`; `color-scheme` must list `dark` or it always returns the first. The scheme is inherited, so a subtree can force one (`web.dev`, MDN).
- It supports exactly two values. Four themes (light, dark, Novita, Ayu Mirage) are four value sets, not two. Recommendation: do **not** use it for theme values; set concrete values in each theme block. It is a good fit only if the app ever ships a theme that follows the OS with a single block.
- `color-scheme` is not a styling switch for your own elements: it restyles the canvas, form controls, scrollbars (MDN). Set it in every theme block (`dark` for dark themes) so those match.

### 3.3 Deriving tones

```css
:root, [data-theme] {          /* see pitfall 3 for why [data-theme] is included */
  --hover:        color-mix(in oklab, var(--text) 7%, transparent);
  --selected:     color-mix(in oklab, var(--accent) 16%, var(--surface));
  --accent-hover: color-mix(in oklab, var(--accent), white 12%);
  --danger-soft:  color-mix(in oklab, var(--danger) 16%, var(--surface));
}
```

- `color-mix()` takes percentages that need not sum to 100; below 100 it multiplies alpha, so `color-mix(in srgb, X 50%, transparent)` is "X at 50% opacity" (MDN). The existing stylesheet already uses this form for scrollbars and shift-held highlights.
- MDN advises `oklab`/`oklch` for mixing; `srgb` "produces poorer results such as overly dark or grayish mixes".
- `oklch(from var(--accent) calc(l - .08) c h)` keeps hue and chroma while moving lightness: good for a pressed state, a darker border, a muted text. It does not clamp: out-of-gamut results are possible.

### 3.4 `@property` (optional)

`@property --bg { syntax: "<color>"; inherits: true; initial-value: #f7f4f1; }` types a token, gives a default, and lets `transition: --bg .2s` animate a theme change (MDN notes the typing; animating registered colours is implied, **not demonstrated** on that page). Cost: each token is registered twice (declaration and value), and registered tokens must be resolvable at the registration level. Skip for the first version; add if a cross-fade is wanted.

### 3.5 Cascade layers

The app already has `@layer tokens`. Keep theme blocks in it. Because normal unlayered declarations beat layered ones and the **`!important` order is reversed** (MDN), do not put `!important` in tokens. The one place layers hurt: overrides of third-party CSS that is not in a layer (Radzen, section 6).

---

## 4. Following the system, and no flash

### 4.1 "Auto" beside named themes

Models in use:

- Chrome's guidance: three states, System (no stored value), Light, Dark; settings may offer all three; remove the stored value to mean "system"; do not clear a stored value merely because it equals the OS value; listen for `storage` events across tabs.
- daisyUI: `prefersdark: true` marks the theme used when the OS prefers dark; `default: true` marks the fallback.
- Primer: `data-color-mode="auto"` plus `data-light-theme="X"` and `data-dark-theme="Y"` select which named theme each OS side maps to.
- Chrome and MDN: any script that reads the preference must also listen for the `change` event on `matchMedia("(prefers-color-scheme: dark)")`.

Recommendation for this app: the saved setting is one of `system | light | dark | novita | ayu-mirage` (more later). `theme.js` maps it to a concrete id for the attribute: `system` becomes `dark` or `light` from `matchMedia`, and a listener updates it when the OS changes (the app already has this wiring: `app.js` line 39 `matchMedia(...)`, `theme.js`). If wanted later, store a pair (theme used when the OS is light, theme used when it is dark) in the Primer style. The CSS then needs **no** `@media (prefers-color-scheme)` blocks.

Each theme declares its scheme itself (`color-scheme` inside its block): Ayu Mirage and Novita (if dark) set `dark`; this is also what native controls follow.

### 4.2 WebView2 specifics

`CoreWebView2Profile.PreferredColorScheme` (Auto by default, "follow whatever colour scheme the OS is currently set to") sets the `prefers-color-scheme` media feature and the colour of WebView2's own UI (dialogs, prompts, menus). If the app wants the OS-independent behaviour (a user picks Ayu Mirage while Windows is light), the host can set it to Dark/Light so the context menu and dialogs match the chosen theme. Whether the Photino host exposes this property is **unverified**; the in-page `color-scheme` already covers form controls and scrollbars.

### 4.3 No flash of the wrong theme

Established technique (Next.js "preventing flash before hydration", updated 2026-11-06; Chrome dark-mode-toggle guide): a small **synchronous inline script in `<head>`**, not `type=module`, not `defer`, placed right after the `<meta>`s, that sets `data-theme` on `<html>` before the first paint, with the storage read in `try/catch`. Keep a default `data-theme="light"` in the static HTML so no-script still renders.

The app already has this shape (`index.html` carries `data-theme="light"`, `theme.js` applies the stored value early; its comment says "so the first paint is not light on a dark window"). What must change for named themes: `theme.js` currently treats only `dark|light|system`; it must accept any id from the same list the CSS defines, and fall back to `light` for an unknown or removed id. One list of theme ids should be the single source (a test can compare the ids in `theme.js`, `app.css` and the Settings list).

Because the app reads the saved choice from a user setting (host side), the cleanest first paint is for the host to inject the attribute into `index.html` or a small file `theme.js` reads synchronously; localStorage mirroring is a convenience only (it can be empty in some WebView configurations). Which of these the app uses is for the audit.

Also set `<meta name="color-scheme" content="light dark">` or the stylesheet's `color-scheme` on `:root` so the canvas behind the page is right before the stylesheet applies (Chrome guidance: "may not prevent every flash").

---

## 5. Derived versus hand-picked colours; contrast

### 5.1 When to derive

Derive (with `color-mix`/relative colour) when the value is a *state or tint of a role*:

- hover and pressed backgrounds (text colour at 6-8% over the surface),
- selected row (accent at 12-18% over the surface),
- soft status backgrounds (status colour at 14-18% over the surface),
- accent hover (accent toward white or toward the bg),
- scrollbar thumb (already derived from `--ink-2`),
- Radzen's `-light/-dark/-lighter/-darker` tones (Radzen's Sass does the same: `color.mix($rz-white, $rz-primary, 12%)`, `rgba($rz-primary, .16)`; verified in `dark-base.scss`).

Set by hand when:

- it is an **identity** value (background, surfaces, accent, the status hues, the categorical set): a theme is recognisable by them;
- it is a **content colour on a fill** (`on-accent`, `on-status`, `on-fill`): derivation can land just under the threshold; Material, shadcn, daisyUI, Radzen all ship these as explicit values;
- the official palette is wrong for an app (Ayu's UI text, section 8);
- a derived result would fall in a theme's problem area (very dark backgrounds make a 7% text overlay invisible; check per theme).

Radzen's own `-wcag` files confirm the second point: the WCAG variant is not derived, it is a separately authored small file of explicit values (primary `#bf540d`, etc.).

### 5.2 Contrast requirements

- WCAG 2.2 (Recommendation 2024-12-12): **1.4.3** text 4.5:1, large text 3:1; **1.4.11** UI components and meaningful graphics 3:1 against adjacent colours.
- Primer ADR-010 (2024-12-10) turns this into a table that fits an app: text vs any background it sits on 4.5:1; placeholder 4.5:1; disabled and decorative text no requirement; focus border 3:1; border of an *empty* control 3:1 (a control with text that passes needs none); decorative dividers none; states that appear together (selected vs unselected) 3:1.
- APCA (candidate for WCAG 3; the page's status line says it is not part of WCAG 2.x; date of the page **unverified**): body text Lc 75 minimum (90 preferred), content text Lc 60, large 45, placeholder/disabled 30, non-text 15. It exists because WCAG 2 ratios overstate contrast for dark colours. Use it as a second opinion on dark themes; keep WCAG 2.2 as the pass/fail rule. Radix uses APCA internally (Lc 60 and 90 for its steps 11-12).
- How theme authors check: Primer runs an "A11y contrast check" workflow in CI over its token files; Radix guarantees by construction; Material derives tones so a tone distance implies a ratio (the "tone difference 40 is about 3:1 and 50 about 4.5:1" rule is from memory, **unverified**); Catppuccin leaves it to the user ("use your own judgement"); VS Code themes are checked by eye plus high-contrast variants.
- For this app: a deterministic test (no rendering): parse the theme blocks of `app.css`, resolve the derived tokens, and assert the ratio of a fixed list of pairs per theme (section 9.4). A failing pair names the theme and the tokens. This also keeps the hidden-app screenshots as a check of *looks*, not of *numbers*.

---

## 6. Theming Radzen by variables

Verified from `radzenhq/radzen-blazor` (v13.3.0, 2026-11-24), `Radzen.Blazor/themes/` and `RadzenTheme.razor(.cs)`, `ThemeService.cs`:

- `<RadzenTheme Theme="x" />` emits `<link id="radzen-theme-link" rel="stylesheet" href="...">` and, with `Wcag`, a second `<link id="radzen-wcag-theme-link">` **after** it. The theme CSS is a `:root { --rz-...: value; }` block (`_css-variables.scss` loops maps into `:root`), and `ThemeService` can switch themes at runtime and persist (cookie or query string services exist).
- The `*-wcag.scss` files declare `:root, .rz-dark { --rz-base-50: ...; --rz-primary: #bf540d; --rz-primary-light: ...; --rz-on-primary: #ffffff; --rz-link-color: ...; ... }`: Radzen's supported "second theme" is exactly a small override file of `--rz-*` loaded after the base. A host stylesheet can do the same.
- Families a bridge must set to recolour Radzen from the app's roles:
  - surfaces and text: `--rz-body-background-color` (default `var(--rz-base-900)` in dark), `--rz-base-background-color` (`var(--rz-base-800)`), `--rz-text-title-color`, `--rz-text-color`, `--rz-text-secondary-color`, `--rz-text-tertiary-color`, `--rz-text-disabled-color`, `--rz-link-color`, `--rz-link-hover-color`;
  - the neutral scale `--rz-base-50 ... --rz-base-900` (+ `-light -lighter -dark -darker`): grids, inputs, panels and borders reference these;
  - hues `--rz-primary`, `--rz-secondary`, `--rz-info`, `--rz-success`, `--rz-warning`, `--rz-danger`, each with `-light -lighter -dark -darker` and content colours `--rz-on-<hue>` (and `-light` ...);
  - `--rz-series-1..24` (charts), `--rz-border-width`, `--rz-border-radius`, input borders (`--rz-input-border` etc.).
- The `-light`/`-dark` tones are computed at Sass build time (`primary #bf540d` becomes `-light #c7692a`: 12% white, 88% primary; checked arithmetically). **Overriding `--rz-primary` alone leaves the old tones.** Bridge them in CSS with the same formulas: `--rz-primary-light: color-mix(in srgb, white 12%, var(--rz-primary))`, `--rz-primary-dark: color-mix(in srgb, black 8%, var(--rz-primary))`, `--rz-primary-darker: ... black 25%`, `--rz-primary-lighter: color-mix(in srgb, var(--rz-primary) 16%, transparent)` (percentages per hue in the Sass: primary 12/16/8/25, info 16/20/16/25, success 16/16/16/25).
- Cascade trap: Radzen's CSS is not in a layer. Per MDN, unlayered normal declarations beat layered ones. A bridge written inside `@layer tokens` loses; write it **unlayered** (or in a layer declared after, which still loses to unlayered; so unlayered), as `:root[data-theme] { --rz-...: var(--bg); }` after the Radzen `<link>`.
- Some Radzen rules use literal colours or `rgba()` computed at build time, so a few details (shadows on some components, icon opacities) may not follow variables: **unverified** without rendering each Radzen component the app uses; verify in the hidden app per theme.
- The Material Symbols font bundled with Radzen is already used by the app (not a colour concern).

---

## 7. Mapping an editor theme to app roles

VS Code theme colour ids people use for each app role (guide: `theme-color` reference, the `color-theme` extension guide; Ayu Mirage values from `vscode-ayu/ayu-mirage.json`, 1.6.0):

| App role | VS Code id(s) | Ayu Mirage value |
|---|---|---|
| canvas / sidebar surface | `sideBar.background`, `activityBar.background`, `panel.background` | `#1F2430` |
| main content surface (cards, editor) | `editor.background`, `tab.activeBackground`, `input.background` | `#242936` |
| sunk shell (title/status bars) | `titleBar.activeBackground`, `statusBar.background` | `#181C26` |
| raised (menus, dropdowns, hovers) | `editorWidget.background`, `dropdown.background`, `editorHoverWidget.background` | `#282E3B` (menus `#1C212C`) |
| text | `editor.foreground`, `input.foreground` | `#FEFAEC` at 77% = `#CCCAC2` flattened |
| UI text / muted | `foreground`, `descriptionForeground`, `sideBar.foreground` | `#D3E3FC` at 45% = `#707A8C` flattened |
| disabled / placeholder | `disabledForeground`, `input.placeholderForeground` | `#D3E3FC` at 23% |
| divider borders | `panel.border`, `sideBar.border`, `tab.border`, `widget.border` | `#171B24` |
| control border | `input.border`, `dropdown.border` | `#D3E3FC` at 9% (on `#242936` = `#343A48`) |
| focus ring | `focusBorder` | `#FFCC66` |
| accent / primary button | `button.background`, `progressBar.background`, `textLink.foreground`, `panelTitle.activeBorder` | `#FFCC66` |
| text on accent | `button.foreground`, `activityBarBadge.foreground` | `#735923` |
| soft badge | `badge.background` + `badge.foreground` | `#FFCC6633` on `#FFCC66` (accent at 20% fill + accent text) |
| hover row / selected row | `list.hoverBackground`, `list.activeSelectionBackground`, `list.inactiveSelectionBackground` | `#63759926` (15%), `#69758C1F` (12%) |
| text selection | `selection.background`, `editor.selectionBackground` | `#409FFF40` |
| error / warning / info | `errorForeground`, `editorError.foreground`, `editorWarning.foreground`, `editorInfo`/`inputValidation.infoBorder` | `#FF6666`, `#FFCC66`, `#5CCFE6` |
| added / modified / removed | `gitDecoration.untrackedResourceForeground` (added), `...modifiedResourceForeground`, `...deletedResourceForeground`, `editorGutter.*Background` | `#87D96C`, `#80BFFF`, `#F27983` |
| shadow | `widget.shadow` | `#00000033` |
| categorical colours | syntax token colours and terminal ANSI colours | the eleven syntax colours (section 8) |

Method (editor theme to app theme): (1) pick the three surface steps from sidebar/editor/shell; (2) text and muted from `editor.foreground` and `foreground`, then *test* them; (3) accent from `button.background`/`focusBorder`; (4) status from `error`, `editorWarning`, git decorations; (5) categorical set from syntax and ANSI hues; (6) fix whatever fails contrast; (7) derive hover/selected from the editor theme's own alpha overlays (they are already translucent washes, which map straight onto `color-mix(... transparent)`).

Editor themes are designed for monospaced text on a dark field at editor sizes; UI chrome has small grey labels, outlines and filled chips that editor themes never have to pass. Expect the "muted text" and "border" roles to need lifting (Ayu does, section 8).

---

## 8. Ayu Mirage: the official palette

### 8.1 Sources, versions, licence

- Palette repository: `ayu-theme/ayu-colors` (default branch `master`), last commit 2026-11-03 ("Make text colors translucent and scale alpha modifiers"). The Mirage definition is `themes/mirage.yaml`; derived palettes are generated into the npm package `ayu` (9.1.0, 2026-09-27; 9.0.0, 2025-12-25). Licence: **MIT**, `license` file: "Copyright (c) Konstantin Pschera".
- VS Code extension: `ayu-theme/vscode-ayu`, `package.json` name `ayu`, publisher `teabyii`, **1.6.0 (2026-11-03)**, licence MIT. Theme files `ayu-mirage.json` (+ `-unbordered`). The CHANGELOG records: 1.2.0 (2026-09-27) modern-UI support, "Ayu Light, Mirage and Dark now always have borders"; 1.4.0 (2026-09-28) unbordered variants return; 1.6.0 (2026-11-03) text and UI text translucent.
- The values below are read from the generated `ayu-mirage.json` of 1.6.0 (flattened over the surface they sit on where translucent).

### 8.2 Version history of the Mirage background (read from the `ayu` npm package)

| Version | Date | UI/common background | Editor background | Foreground (editor / UI) |
|---|---|---|---|---|
| `ayu@6.0.0` | 2018-08-06 | `#212733` | derived from entity colour (`fade(10)`) | `#CCC9C2` |
| `ayu@7.3.2` | 2021-08-12 | `#1F2430` | derived (`brighten(.1)`) | `#CBCCC6` |
| `ayu@8.0.0` / `8.0.1` | 2021-09-02 / 09-05 | `#1F2430` | `#242936` | `#CCCAC2` / `#707A8C` |
| **`ayu@9.x` / vscode-ayu 1.6.0 (current)** | 2025-12-25 / 2026-11-03 | `#1F2430` (`surface.base`) | `#242936` (`surface.lift`) | `#FEFAEC` at 77% (= `#CCCAC2`) / `#D3E3FC` at 45% (= `#707A8C` on `#1F2430`) |

So: the older, classic Mirage background is **`#212733`** (2018); since 2021 it is **`#1F2430`** for the window and **`#242936`** for the editor; 9.x keeps the same flattened values and re-expresses text as translucent. A new `surface` family appears in 9.x: `sunk #181C26`, `base #1F2430`, `lift #242936`, `over #FFFFFF`.

### 8.3 Current Mirage values (hex)

**Editor and UI** (`mirage.yaml`, `ayu-mirage.json`):

| Name | Value | Note |
|---|---|---|
| surface.sunk | `#181C26` | title bar, status bar |
| ui.bg / surface.base | `#1F2430` | side bar, panel, activity bar |
| editor.bg / surface.lift | `#242936` | editor, inputs, active tab |
| ui.panel.bg | `#282E3B` | dropdowns, widgets, hovers |
| ui.popup.bg | `#1C212C` | menus |
| editor.line | `#1A1F29` | current line |
| ui.line | `#171B24` | borders and separators |
| editor.fg | `#FEFAEC` at 77% | = `#CCCAC2` on `#242936`; `#CBC9C1` on `#1F2430` |
| ui.fg | `#D3E3FC` at 45% | = `#707A8C` on `#1F2430` |
| ui.selection.active / normal | `#637599` at 15% / `#69758C` at 12% | hover / selected |
| editor.selection active / inactive | `#409FFF` at 25% / 13% | |
| editor.findMatch active | `#736950` | |
| common.accent.tint | `#FFCC66` | accent |
| common.accent.on | accent darkened (`-L0.39`) = `#735923` in `button.foreground` | text on accent |
| common.error | `#FF6666` | |
| ui.panel.shadow / popup.shadow | `#000000` at 20% / 30% | |

**Syntax** (`syntax:` section; each is a palette base colour):

| tag | func | entity | string | regexp | markup | keyword | special | comment | constant | operator |
|---|---|---|---|---|---|---|---|---|---|---|
| `#5CCFE6` | `#FFCD66` | `#73D0FF` | `#D5FF80` | `#95E6CB` | `#F28779` | `#FFA659` | `#D9BE98` | `#6E7C8F` | `#DFBFFF` | `#F29E74` |

(The older `ayu@8.0.1` had `func #FFD173`, `keyword #FFAD66`, `special #FFDFB3`, `comment #B8CFE6` at 50%, which differ from the current ones.)

**Palette base hues** (`palette:`): gray `#6E7C8F`, red `#F28779`, pink `#F29E74`, orange `#FFA659`, peach `#D9BE98`, yellow `#FFCD66`, green `#D5FF80`, teal `#95E6CB`, indigo `#5CCFE6`, blue `#73D0FF`, purple `#DFBFFF` (each with five lightness steps l1-l5 generated in OKLCH range 0.725-0.925; the l-step hex values were not extracted, only the bases).

**VCS**: added `#87D96C`, modified `#80BFFF`, removed `#F27983`. **Terminal**: ansiRed `#F28273`, green `#87D96C`, yellow `#FCCA60`, blue `#6ACDFF`, magenta `#DDBBFF`, cyan `#93E2C8`, white `#C7C7C7`, brightBlack `#686868`.

### 8.4 Contrast of the official values on Mirage (computed)

| Pair | Ratio | Verdict |
|---|---|---|
| `#CCCAC2` on `#242936` / on `#1F2430` | 8.85 / 9.36 | pass |
| official UI text `#707A8C` on `#1F2430` / `#242936` | 3.58 / 3.50 | **fails AA text** (fine as disabled text) |
| `#FFCC66` on `#242936` | 9.74 | pass |
| official on-accent `#735923` on `#FFCC66` | 4.42 | **fails by 0.08** |
| `#FF6666` on `#242936` | 5.08 | pass |
| `#6E7C8F` (comment grey) on `#242936` | 3.42 | fails text; not needed in an app |
| official input border `#D3E3FC` at 9% on `#242936` | 1.28 | decorative only; fails the 3:1 for an empty control |
| `#171B24` divider on `#242936` | 1.19 | decorative (Ayu draws dividers darker than the surface) |

---

## 9. Recommendation

### 9.1 Token structure (20 hand-set roles + 14 categorical values; 7 derived)

Names are proposals; the "was" column maps the app's current variables (from a quick read of `app.css`; the audit decides the real inventory).

**Surfaces**

| Token | For | Was |
|---|---|---|
| `--bg` | app canvas behind everything, also the title area | `--paper` |
| `--surface` | cards, table rows, inputs, panels | `--card` |
| `--surface-sunken` | wells, table header, code blocks, status/title shell | new (light: derived from bg) |
| `--surface-raised` | menus, popups, dialogs, dropdowns (also draws a shadow) | new |
| `--scrim` | modal backdrop | new |

**Text**

| Token | For |
|---|---|
| `--text` | body and headings (was `--ink`) |
| `--text-muted` | captions, labels, secondary text; must pass 4.5:1 on `--surface` and `--surface-raised` (was `--ink-2`) |
| `--text-faint` | disabled text and decorative text only (no ratio required) |

**Lines**

| Token | For |
|---|---|
| `--border` | firm outlines of empty controls and cards; 3:1 against the surface it sits on (was `--line`) |
| `--border-soft` | dividers and quiet input borders; decorative (was `--line-soft`) |
| `--focus` | focus ring; 3:1 against adjacent colours (was `--accent` in the focus rule) |

**Accent**

| Token | For |
|---|---|
| `--accent` | brand/primary fill, active tab marker, progress |
| `--on-accent` | text and icons on an accent fill; 4.5:1 |
| `--link` | link text (often equal to accent; separate because a dark-text accent such as the light theme's ink cannot colour links) |

**Status** (solid hue = icon, border, small fill; text in that colour must also pass 4.5:1 where it is text)

| Token | For |
|---|---|
| `--danger` | errors, delete, alerts (was `--alert`) |
| `--warning` | caution, overdue |
| `--success` | done, added (was `--ok`) |
| `--info` | neutral information |
| `--on-status` | text on a solid status fill (was `--on-alert`) |

**Effects**

| Token | For |
|---|---|
| `--shadow` | the colour of the app's hard offset shadows (was `--shadow`) |

**Categorical chips** (the 13 pastel fills; each fill always carries `--on-fill` text)

`--c-peach --c-sun --c-sage --c-sky --c-lilac --c-stone --c-mint --c-rose --c-teal --c-sand --c-green --c-indigo --c-orchid` (**13 fills**, as in the current stylesheet: `--peach` ... `--orchid`) and `--on-fill`. Keep the names; rename only the prefix if wanted.

**Derived once, declared on `:root, [data-theme]`** (not per theme):

| Token | Derivation |
|---|---|
| `--hover` | `color-mix(in oklab, var(--text) 7%, transparent)` |
| `--selected` | `color-mix(in oklab, var(--accent) 16%, var(--surface))` |
| `--accent-hover` | `color-mix(in oklab, var(--accent), white 12%)` in dark themes, `black 12%` in light (a per-scheme value; or derive from lightness with a relative colour) |
| `--danger-soft`, `--warning-soft`, `--success-soft`, `--info-soft` | `color-mix(in oklab, var(--<status>) 16%, var(--surface))` |

Count: 5 surfaces + 3 text + 3 lines + 3 accent + 5 status + 1 effect = **20 hand-set roles**, + 13 chips + `--on-fill` = **34 values per theme**, + `color-scheme`. Inside the 20-35 target.

### 9.2 Mechanics for this app

1. One `:root { ... }` block with every role (the light theme's values, and `color-scheme: light`). It is the fallback for unknown or incomplete themes.
2. One block per further theme, `:root[data-theme="<id>"] { color-scheme: dark|light; <every role>; }`, all in `@layer tokens`. Adding a theme means adding one block and one entry in the list of ids; no other CSS changes.
3. The attribute on `<html>` is always a **concrete** id. "System" is a saved value resolved by script (`light`/`dark` from `matchMedia`, re-resolved on `change`). Delete the duplicated `@media (prefers-color-scheme: dark)` block and the separate `--alert` pairs.
4. First paint: keep `data-theme="light"` in the static HTML and the synchronous head script; accept any id from the one list; unknown id falls back to `light`.
5. Derived tokens (above) are declared once on `:root, [data-theme]`; all component CSS reads roles and derived tokens only. Remaining literal colours in components are bugs (audit will list them).
6. Radzen bridge: one unlayered block after the Radzen link, `:root[data-theme] { --rz-body-background-color: var(--bg); --rz-base-background-color: var(--surface); --rz-text-color: var(--text); ... --rz-primary: var(--accent); --rz-primary-light: color-mix(...); ... }`, with the base scale `--rz-base-50...900` generated by mixing `--text` into `--surface` (0%..100% steps). Order: Radzen CSS, then app CSS.
7. Settings: the list of themes is data (id, label, scheme, and the three swatch colours); the choice is saved as a user setting; applying it sets the attribute (and the host's `PreferredColorScheme` if exposed).
8. Tests (deterministic, no browser): (a) every theme block defines every token of the contract; (b) the theme ids in `theme.js`, `app.css` and the Settings list are the same set; (c) the contrast pairs of 9.4 pass per theme; (d) no hex colour literal outside the token blocks (the audit's list drives it).

### 9.3 Ayu Mirage mapped onto the tokens

"official" = taken as is from the palette; "derived" = computed from official values (method given); contrast figures are computed.

| Token | Value | Source |
|---|---|---|
| `color-scheme` | `dark` | |
| `--bg` | `#1F2430` | official `ui.bg` / `surface.base` |
| `--surface` | `#242936` | official `editor.bg` / `surface.lift` |
| `--surface-sunken` | `#181C26` | official `surface.sunk` |
| `--surface-raised` | `#282E3B` | official `ui.panel.bg` (menus in VS Code are `#1C212C`; raised is lighter here so it reads above the surface) |
| `--scrim` | `#181C26` at 60% (`rgb(24 28 38 / .6)`) | derived from `surface.sunk` |
| `--text` | `#CCCAC2` | official `editor.fg` flattened; 8.85:1 on surface |
| `--text-muted` | `#8D99AD` | derived: official UI text hue `#D3E3FC` at **60%** instead of 45% over `#242936`; 5.05:1 on surface, 4.72:1 on raised, 5.91:1 on sunken. The official `#707A8C` is 3.5:1 |
| `--text-faint` | `#707A8C` | official `ui.fg` (disabled/decorative only) |
| `--border` | `#6A7385` | derived: `#D3E3FC` at 40% over `#242936`; 3.05:1 (the official input border is 1.28:1) |
| `--border-soft` | `#171B24` | official `ui.line` (Ayu's own divider; alternative `#343A48` = official input border flattened) |
| `--focus` | `#FFCC66` | official `focusBorder`; 9.74:1 |
| `--accent` | `#FFCC66` | official `common.accent.tint` |
| `--on-accent` | `#6B5220` | derived: official `#735923` deepened until 4.5 is passed (4.94:1; official gives 4.42:1). Alternative: `#1F2430` (10.41:1) |
| `--link` | `#FFCC66` | official `textLink.foreground` |
| `--danger` | `#FF6666` | official `common.error`; 5.08:1 on surface |
| `--warning` | `#FFA659` | official palette orange (keyword); 7.51:1. (Ayu's `editorWarning` is the accent yellow; a separate hue keeps warning and accent apart) |
| `--success` | `#87D96C` | official `vcs.added`; 8.42:1 |
| `--info` | `#5CCFE6` | official `inputValidation.infoBorder` / tag; 7.97:1 |
| `--on-status` | `#181C26` | official `surface.sunk`; 5.96:1 on `#FF6666`, 11.5:1 on yellow |
| `--shadow` | `#000000` | official shadow base (`#000000` at 20-30%) |
| `--on-fill` | `#181C26` | official `surface.sunk` |
| `--c-peach` | `#F29E74` | palette pink (operator); 8.04:1 with on-fill |
| `--c-sun` | `#FFCD66` | palette yellow (func); 11.5:1 |
| `--c-sage` | `#D5FF80` | palette green (string); 14.96:1 |
| `--c-sky` | `#73D0FF` | palette blue (entity); 9.89:1 |
| `--c-lilac` | `#DFBFFF` | palette purple (constant); 10.57:1 |
| `--c-stone` | `#9FAEC4` | derived: palette gray `#6E7C8F` 50% + `#D3E3FC` in oklab (the gray itself gives only 4.0:1 with on-fill); 7.56:1 |
| `--c-mint` | `#95E6CB` | palette teal (regexp); 11.73:1 |
| `--c-rose` | `#F28779` | palette red (markup); 6.91:1 |
| `--c-teal` | `#5CCFE6` | palette indigo (tag); 9.35:1 |
| `--c-sand` | `#D9BE98` | palette peach (special); 9.55:1 |
| `--c-green` | `#87D96C` | `vcs.added`; 9.87:1 |
| `--c-indigo` | `#B0C9FF` | derived: palette blue + purple 50/50 in oklab (Ayu has no indigo); 10.26:1 |
| `--c-orchid` | `#EAA2C7` | derived: purple 55% + `vcs.removed` `#F27983` 45% in oklab (Ayu has no magenta); 8.51:1 |
| `--hover` (derived default) | official `#637599` at 15% over the surface | official `ui.selection.active`; or the default derivation |
| `--selected` | official `#69758C` at 12%, or default derivation (accent 16%) | |

Notes: `--c-sky` / `--c-teal` / `--c-indigo` and `--c-peach` / `--c-rose` / `--c-sand` are neighbours in hue; chips must stay legible by their text label, not by colour alone (WCAG 1.4.1, outside this file's scope). All chip fills are light and the on-fill is dark, matching the existing dark theme's pattern.

### 9.4 Contrast pairs to test per theme

Text: `text/bg`, `text/surface`, `text/surface-raised`, `text-muted/bg`, `text-muted/surface`, `text-muted/surface-raised`, `link/surface`, `on-accent/accent`, `on-status/{danger,warning,success,info}`, `on-fill/each chip` (all >= 4.5). UI parts: `border/surface` (>= 3.0; the intended control outline), `focus/surface`, `focus/bg`, `accent/surface`, `danger,success,warning,info/surface` (>= 3.0). Not tested: `text-faint`, `border-soft`.

### 9.5 Pitfalls

1. **Official editor palettes fail app contrast.** Ayu's UI text (3.5:1), on-accent (4.42:1), borders (1.2-1.3:1) and gray (3.4:1) are all below the app thresholds. Take official values for identity, test the rest, lift what fails (section 9.3 shows how).
2. **`!important` and layers.** Unlayered rules beat layered ones (MDN `@layer`), so a Radzen bridge inside `@layer tokens` is silently ignored. Write it unlayered, after Radzen's link.
3. **Derived tokens on `:root` do not follow a nested theme.** A custom property that references `var(--x)` is computed on the element where it is declared and inherited as that computed value. If a Settings preview card sets `data-theme="ayu-mirage"` on a `div`, derived tokens declared only on `:root` still hold the root theme's mix. Declare derived tokens on `:root, [data-theme]`.
4. **Radzen stale tones.** `--rz-primary-light` and the other `-light/-dark/-lighter/-darker`, and many `--rz-base-*` steps, are precomputed; override the whole family or re-derive them with `color-mix`.
5. **Duplicate dark blocks.** The current stylesheet repeats the dark values under `@media (prefers-color-scheme: dark)` and under `[data-theme="dark"]`; a third theme multiplies the copies and they will drift. Resolve "system" in script instead.
6. **Two lists of theme ids (CSS and script) will diverge.** Compare them in a test; an unknown stored id (a theme removed in a later version) must fall back to `light`.
7. **`light-dark()` needs `color-scheme`.** Without `dark` in `color-scheme` it always returns the first value. Not recommended here, but if it is used anywhere, every theme block must declare the scheme.
8. **`color-scheme` is per theme, not global.** A dark theme without `color-scheme: dark` shows light scrollbars, form controls and a white canvas flash on overscroll. The app already sets it for light and dark; each new block needs it too.
9. **Hard-coded colours in components** (SVG fills, inline styles in Razor, chart colours, the JS drag/resize helpers, `border: ... #fff`). They will look right in two themes and wrong in the third. The audit lists them; a test that forbids hex literals outside token blocks keeps it fixed.
10. **Flash.** A deferred or module theme script, or reading the saved theme only after Blazor starts, paints the default first. Synchronous head script only; no dependence on the Blazor circuit.
11. **Alpha tokens compose badly over arbitrary backgrounds.** Ayu 9.x expresses text as translucent colours (`#FEFAEC` at 77%); copy the **flattened** value for an app role that sits on several surfaces, or contrast changes from surface to surface.
12. **`color-mix` percentages and `transparent`**: mixing with `transparent` in `srgb` gives a premultiplied look; use `oklab` for colours and `transparent` only for alpha washes like hover.
13. **Relative colour syntax is not clamped** and does not accept `%` in `calc(l + 20%)`; keep derivations in `color-mix` unless a lightness shift is needed.
14. **Selection colours.** `::selection` should be set from a role (accent at 25-30%) or text selection stays the browser blue in every theme.
15. **High-contrast / forced-colors mode** replaces author colours; confirm with `forced-colors: active` that borders and focus rings still show (use `outline`, not only `box-shadow`). Not verified in this research.
16. **Licence notices.** Ayu's palette is MIT; hex values are facts, but if the palette file or theme JSON is copied, keep the copyright and permission text (a THIRD-PARTY-NOTICES line is enough). Same for Novita if it is not the owner's own code.

---

## 10. Sources

Dates are the page's own "last modified"/version/commit date, as found 2026-11-25. "(read)" = content read directly by me; "(summary)" = read through a summarising fetch; "(undated)" = the page shows no date.

**Standards and platform**

1. MDN, `light-dark()`, last modified 2026-11-10; Baseline widely available since May 2024 (summary). https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Values/color_value/light-dark
2. MDN, `color-scheme`, last modified 2026-07-21 (summary). https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Properties/color-scheme
3. MDN, Relative colors, last modified 2026-11-10 (summary). https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_colors/Relative_colors
4. MDN, `color-mix()`, last modified 2026-11-10; widely available since May 2023 (summary). https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/Values/color_value/color-mix
5. MDN, `@property`, last modified 2026-05-01; Baseline 2024 newly available (summary). https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/At-rules/@property
6. MDN, `@layer`, last modified 2026-04-20; widely available since March 2022 (summary). https://developer.mozilla.org/en-US/docs/Web/CSS/Reference/At-rules/@layer
7. mdn/browser-compat-data, `css/types/color.json`, `css/properties/color-scheme.json`, `css/at-rules/{property,layer}.json`, repository head 2026-11-24 (read; the Chrome versions in section 3). https://github.com/mdn/browser-compat-data
8. W3C, CSS Color Module Level 5, Working Draft 2026-11-16 (summary). https://www.w3.org/TR/css-color-5/
9. W3C, CSS Color Adjustment Module Level 1, Candidate Recommendation Snapshot 2025-12-16 (summary). https://www.w3.org/TR/css-color-adjust-1/
10. W3C, WCAG 2.2 Recommendation 2024-12-12, SC 1.4.3 and 1.4.11 (summary). https://www.w3.org/TR/WCAG22/
11. Microsoft Learn, `CoreWebView2Profile.PreferredColorScheme`, updated 2026-11-21 (read). https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2profile.preferredcolorscheme
12. Next.js, "Preventing flash before hydration", lastUpdated 2026-11-06 (read). https://nextjs.org/docs/app/guides/preventing-flash-before-hydration
13. Chrome for Developers, "Dark mode toggles", (undated) (summary). https://developer.chrome.com/docs/modern-web-guidance/guides/ui-components/dark-mode-toggle
14. Chrome for Developers, "Dark mode" guide, (undated) (summary). https://developer.chrome.com/docs/modern-web-guidance/guides/visual-design/dark-mode
15. web.dev, "CSS color-scheme-dependent colors with light-dark()", Baseline newly available 2024-05-13; page undated (summary). https://web.dev/articles/light-dark

**Design systems and theme definitions**

16. GitHub Primer, `primer/primitives` 11.10.0 released 2026-07-30; `dist/css/functional/themes/dark.css` (959 properties; `data-color-mode` / `data-dark-theme` selectors) (read). https://github.com/primer/primitives
17. Primer ADR-010 "Color contrast requirements", 2024-12-10 (read). https://github.com/primer/primitives/blob/main/contributor-docs/adrs/adr-010-color-contrast.md
18. Primer, colour tokens page (undated; counts are a tool estimate, unverified). https://primer.style/product/primitives/color/
19. Radix Colors, "Understanding the scale" (docs source last changed 2026-06-11; 12 steps, APCA guarantees on steps 11-12) (summary). https://www.radix-ui.com/colors/docs/palette-composition/understanding-the-scale
20. Material Web, `docs/theming/color.md`, last commit 2026-08-03, freshness reviewed 2026-07-31 (read). https://github.com/material-components/material-web/blob/main/docs/theming/color.md
21. shadcn/ui, Theming, last commit 2026-03-25 (summary). https://ui.shadcn.com/docs/theming
22. daisyUI 5 Themes, release 5.8.2 2026-11-20 (summary). https://daisyui.com/docs/themes/
23. Catppuccin, Style Guide, last commit 2025-10-01 (read); `catppuccin/palette` `palette.json` v1.8.0, 2026-03-21, MIT (read). https://github.com/catppuccin/catppuccin/blob/main/docs/style-guide.md , https://github.com/catppuccin/palette
24. Open Props, repository `src` last commit 2026-10-11 (summary; the colour file itself last changed 2022, so not counted for that content). https://open-props.style/
25. Tailwind CSS v4, "Theme variables", last commit 2026-07-28 (summary). https://tailwindcss.com/docs/theme
26. VS Code, Theme Color reference, last commit 2026-11-22 (summary; total id count not seen). https://code.visualstudio.com/api/references/theme-color
27. VS Code, Color Theme extension guide, last commit 2026-11-18 (summary). https://code.visualstudio.com/api/extension-guides/color-theme
28. APCA in a Nutshell, Lc thresholds (summary; page undated, copyright 2022). https://git.apcacontrast.com/documentation/APCA_in_a_Nutshell.html

**Radzen**

29. `radzenhq/radzen-blazor` v13.3.0 (2026-11-24): `Radzen.Blazor/themes/{dark-base,dark,dark-wcag}.scss`, `_css-variables.scss` (dark-wcag last commit 2026-11-09) (read). https://github.com/radzenhq/radzen-blazor/tree/master/Radzen.Blazor/themes
30. `radzen-blazor` `RadzenTheme.razor`, `RadzenTheme.razor.cs`, `ThemeService.cs` (last commit 2026-10-02) (read). https://github.com/radzenhq/radzen-blazor/blob/master/Radzen.Blazor/ThemeService.cs
31. Radzen Blazor "Get started" (theme list, WCAG option; (undated)) (summary). https://blazor.radzen.com/get-started

**Ayu**

32. `ayu-theme/ayu-colors`, `themes/mirage.yaml`, `src/scheme.d.ts`, `license` (MIT); last commit 2026-11-03 (read). https://github.com/ayu-theme/ayu-colors
33. `ayu-theme/vscode-ayu` 1.6.0 (2026-11-03): `ayu-mirage.json`, `CHANGELOG.md`, `package.json` (MIT) (read). https://github.com/ayu-theme/vscode-ayu
34. npm `ayu` 9.1.0 (2026-09-27), 9.0.0 (2025-12-25): `dist/mirage.js` via unpkg (read). https://www.npmjs.com/package/ayu , https://unpkg.com/ayu@9.0.0/dist/mirage.js

**Older, for the version history only (before 2024; not counted)**

35. npm `ayu` 6.0.0 (2018-08-06), 7.3.2 (2021-08-12), 8.0.0 / 8.0.1 (2021-09-02 / 09-05) `dist/mirage.js` via unpkg (read). https://unpkg.com/ayu@6.0.0/dist/mirage.js

Count: 34 numbered sources dated 2024 or later in content (35 is older, history only). Of these, 27 carry a verifiable date (items 1-12, 16, 17, 19-27, 29, 30, 32-34); items 13, 14, 15, 18, 28, 31 show no page date (stated content only), and items 24 and 19 are dated by their repository commits rather than the page.

---

## 11. Open points for the audit and the owner

- Novita values: not found under `Documents/5-mine`; the finance-tools project must be read (the audit step 1.2). Check there whether Novita is light or dark (sets `color-scheme`), whether it has a categorical set, and whether its text/border/accent pairs pass section 9.4; expect to lift some.
- The runtime Chromium version in the hidden app (CDP `Browser.getVersion`): every feature used here needs 123 or newer; `contrast-color()` (147) is deliberately not used.
- Whether the Photino host can set `PreferredColorScheme` so WebView2's own menus follow the chosen theme.
- Which Radzen theme CSS the app loads and which Radzen components are on screen (grid, dropdown, date picker, dialog): each needs a screenshot per theme.
- Whether the saved choice is read host-side (setting) or from localStorage at first paint (the audit of `theme.js`/`MainLayout.razor`).
