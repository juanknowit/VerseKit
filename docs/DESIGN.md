# VerseKit — Design System

This document defines the visual language of the app. **Read it before changing
any UI, and keep new UI consistent with it.** It is the source of truth for
colors, controls, layout, and the platform constraints we have already hit and
solved — so we don't relitigate them.

The whole theme lives in **`src/VerseKit.App/App.axaml`** as application-level
`Styles` and `Resources`. Plugins inherit these automatically (their views render
inside the same app), so a plugin button styled `Classes="Danger"` looks identical
to one in the shell. **Do not redefine these styles per-view** — extend the shared
ones.

---

## 1. Principles

1. **macOS-native, light, calm.** Mirror Apple's Tahoe-era system apps (Finder,
   System Settings): white floating panels, soft rounding, restrained color.
2. **Color carries meaning, not decoration.** Surfaces are white/grey; color is
   reserved for state (green = connected/publish) and intent (red = destructive).
3. **Readability over flourish.** This is a productivity tool used for long
   sessions against production data. Legibility and clear hierarchy beat visual
   effects. (We tried glass and heavy shadows; both lost to clarity — see §8.)
4. **One shared theme.** Everything is driven from `App.axaml`. New controls reuse
   the tokens and style classes below.
5. **Fluid, purposeful motion.** Following Apple's *Designing Fluid Interfaces*:
   respond on pointer-down, animate state changes so they read as continuous,
   keep every animation interruptible, and exit along the path you entered.
   Motion explains *what changed and where it came from* — never decoration.
   Always honour macOS **Reduce Motion**. See §7.

---

## 2. Design tokens

Defined in `App.axaml` → `Application.Resources`. Reference fixed tokens with
`{StaticResource Key}`, and accent/background tokens (see below) with
`{DynamicResource Key}` so they recolour live; never hard-code these hexes in a view.

### Active tokens

| Token | Value | Use |
|---|---|---|
| `AccentColor` / `AccentBrush` | `#007AFF` | macOS system blue. Focus, accent dot, progress. |
| `SuccessBrush` | `#34C759` | Connected state, success. |
| `TextPrimaryBrush` | `#1C1C1E` | Primary text. |
| `TextSecondaryBrush` | `#6E6E73` | Secondary/label text. |
| `PillEdgeBrush` | `#E2E2E7` | The faint static edge on white pill buttons & inputs. |
| `DangerTintBrush` | `#FCE9E8` | Destructive button fill (Delete). |
| `DangerTintHoverBrush` | `#FADAD8` | Destructive hover. |
| `DangerTintPressedBrush` | `#F5C7C4` | Destructive pressed. |
| `DangerTextBrush` | `#D70015` | Destructive text. |
| `ControlRadius` | `8` | Small controls / list items / badges. |
| `CardRadius` | `14` | Floating panels (sidebar, content card). |

### Literal values used in styles (intentional, keep consistent)

| Where | Value |
|---|---|
| Pill button / input corner radius | `18` (fully rounded at ~36px height) |
| Button hover fill | `#E9E9EE` |
| Button pressed fill | `#DEDEE4` |
| Modal sheet (`FormCard`) radius | `16` |
| Toolbar / bar surfaces | `#F8F8FA` |
| Hairline separators / borders | `#E5E5EA` |
| Connected chip fill | `#E3F5E9` |
| Active connection dot | `#26A641`; inactive `#C0C0C0` |

### Accent-derived tokens (themed — reference with `{DynamicResource}`)

The accent colour is **user-selectable** (Settings → Theme; eight presets,
persisted to `~/.config/versekit/settings.json`). `ThemeManager` overrides the base
accent *and everything derived from it* at runtime, so anything that should follow
the accent must use `{DynamicResource}`, never `{StaticResource}`:
`AccentColor`/`AccentBrush`, `AccentHoverBrush`, `AccentPressedBrush`,
`TintBrush`/`TintHoverBrush`/`TintPressedBrush` (selection washes),
`AccentTextBrush`, `SelectionBrush`, and the Fluent `SystemAccentColor*` shades.

### Background tokens (themed)

Settings → Background offers **Glass** (transparent over `AcrylicBlur` — the
default), **Theme** (an opaque accent-derived diagonal gradient), or **White**.
`ThemeManager` sets `WindowBackgroundBrush` and `TitleBarForegroundBrush` from the
saved choice; reference both with `{DynamicResource}`.

### Legacy / deprecated tokens (do not use in new work)

Leftovers from abandoned experiments (filled fields, coloured success buttons).
They remain only so nothing breaks; **prefer the active tokens** and feel free to
delete these in a cleanup pass:
`SuccessTintBrush`/`*Hover`/`*Pressed`, `SuccessTextBrush`, `FieldBorderBrush`,
`FieldFillBrush`, `FieldFillHoverBrush`.

---

## 3. Buttons

All action buttons are **white, fully-rounded pills** with a faint static edge
(`PillEdgeBrush`) and a clear grey hover. The edge is constant — it does **not**
appear/intensify on hover or focus. No drop shadows (see §8).

| Class | Look | Use for |
|---|---|---|
| *(default, no class)* | White pill, `Medium` weight | Secondary actions (Cancel, Check Syntax, New, Load) |
| `Primary` | White pill, `SemiBold` | The main affirmative action (Connect, Save, Publish). Weight is the only emphasis — no color. |
| `Success` | White pill, `SemiBold` | Alias of Primary; retained for semantic intent (Publish). |
| `Danger` | Red tint fill, red text | Destructive only (Delete). |
| `IconBtn` | Transparent, no edge, grey glyph | Title-bar / chrome icon buttons (settings cog). |
| `SidebarProfile` | Transparent row, hover wash | Sidebar list rows. |
| `DeleteBtn` | Transparent, hover-revealed | Inline row affordances (edit pencil). |

Rules:
- **At most one `Primary`/`Success` per button group.** Everything else is default white.
- **Red is exclusively destructive.** Never use `Danger` for a normal action.
- Button text is always `TextPrimaryBrush` except `Danger` (red). No white-on-color.
- Standard padding `16,7`; group spacing `8px`.

---

## 4. Inputs (TextBox, ComboBox)

Match the buttons: **white pills, `18` radius, `PillEdgeBrush` edge, no border
change on hover or focus.** Filled-grey and accent-focus-ring approaches were tried
and rejected. Focus is indicated by the caret and selection only — keep it quiet.

- Min height `36`, padding `14,8`.
- `FocusAdorner` is removed (`{x:Null}`) so no square focus rectangle is drawn over
  the rounded field.
- Placeholder text uses `PlaceholderText` (not the obsolete `Watermark`).

---

## 5. Layout & surfaces

- **Window:** `TransparencyLevelHint="AcrylicBlur"` (always on), extended client
  area, background = `{DynamicResource WindowBackgroundBrush}` driven by the
  Background setting (Glass = transparent so the blurred desktop shows in the gaps
  around the cards; Theme = opaque accent gradient; White). Title-bar text/icons
  bind to `TitleBarForegroundBrush` to stay legible on all three — see §8.
- **Title bar:** 52px row. The grid stays hit-testable; only the centered title is
  `IsHitTestVisible="False"` so the window still drags but the cog stays clickable.
- **Floating cards:** solid white, `CardRadius` (14), **no drop shadow**, with a
  margin so the background shows around them. This is the sidebar and the content
  area. (Earlier builds shadowed the cards; on the lighter Theme/White backgrounds
  it read as a heavy halo, so it was removed — the margin and background contrast
  separate them instead. See §8.)
- **Bars** (toolbars, action bars): `#F8F8FA` fill, `#E5E5EA` hairline on the
  dividing edge, compact padding (`16,10` top bar, `12,8` action bar).
- **Modal sheets** (`Border.FormCard`): white, radius `16`, `BoxShadow="0 6 20 2 #2E000000"`,
  centered, **no dim backdrop** (the card shadow alone separates it). A ✕ in the
  top-right is the dismiss control — do **not** add a duplicate Cancel button.
  Host the card in a `Panel.SheetOverlay` driven by `behaviors:Sheet.IsOpen`
  (never a bare `IsVisible` binding) so it opens and closes with motion, and
  set the card's `RenderTransformOrigin` toward the control that opened it
  (default `100%,0%` = the Settings cog; the Connection sheet uses `0%,0%`, the
  Plugins sheet `0%,50%` — both opened from the sidebar). See §7.
- **Status chip** (`Border.StatusChip`): neutral grey; `.connected` class swaps to a
  solid pale-green fill (`#E3F5E9`). No animation.
- **Sidebar list** (`ListBox.SidebarList`): selection is a pale accent tint
  (`{DynamicResource TintBrush}`, derived from the chosen accent) with dark text,
  Finder-style — not a saturated fill.

---

## 6. Typography

System font (`-apple-system` equivalent via Avalonia default). Sizes in use:

| Context | Size / weight |
|---|---|
| Sheet / dialog title | 17 SemiBold (modal), 16 SemiBold (About) |
| Body / control text | 13 |
| Labels, secondary | 12–13, `TextSecondaryBrush` |
| Section headers (`SidebarHeader`) | 11 SemiBold, letter-spaced, `TextSecondaryBrush` |
| Badges / chips | 9–11 Bold |
| Code editor | 13, `Cascadia Code, SF Mono, Menlo, monospace` |

**Tracking (`LetterSpacing`) is size-specific — never one value for all sizes.**
Apple's system font wants text tightened as it grows and opened up when small
and uppercase. Set it inline next to the `FontSize` (plugins included, so it
works on any host):

| Size | `LetterSpacing` |
|---|---|
| 17 (sheet titles) | `-0.4` |
| 16 (empty-state titles, About) | `-0.3` |
| 15 | `-0.2` |
| 14 (detail headers) | `-0.15` |
| 13 and body text | `0` (leave unset) |
| 9–11 uppercase headers / badges | `+0.5`–`0.6` |

Display sizes (20+) are left at `0`: we can't rely on the renderer switching to
SF Pro Display's optical size, so Apple's display tracking values don't apply.

---

## 7. Iconography & motion

- **Icons:** simple Unicode glyphs (⚙ ✎ ✕ ↑ ＋) at low-key greys, not an icon font.
  Web-resource type badges are 3-letter labels on a colored rounded rect
  (`WebResourceItem.TypeColor`).
- **App icon:** generated by `scripts/create-icon.py` -> a dotted "global data"
  globe on a blue squircle, with the wordmark "VERSE" spaced wide across the centre
  so it reads as the equator line. Re-run that + `generate-icon.sh` to change it.
- **Motion:** see below.

### Motion

Motion follows Apple's fluid-interface rules (WWDC 2018 *Designing Fluid
Interfaces*), translated to Avalonia. All of it lives in the **Motion** section
of `App.axaml`; reuse those classes rather than writing per-view animations.

**Rules**

1. **Respond on pointer-down.** Feedback happens the instant something is
   pressed, never on release. Don't add delays, debounces or "wait for the
   animation" on the input path.
2. **Interruptible, from the current value.** Use `Transitions` (they start from
   the live on-screen value), so toggling mid-animation reverses smoothly. Never
   block input while something animates.
3. **Critically damped springs by default** (damping ratio `1.0`, no overshoot).
   Bounce is reserved for gestures that carry momentum (a flick or throw) — we
   have none today, so nothing bounces.
4. **Spatial consistency.** Things leave the way they came in, and grow from the
   control that opened them.
5. **Motion explains, it doesn't decorate.** Animate a change in state or place
   (something opening, appearing, being selected). Don't animate hover washes on
   list rows or anything that would slow down scanning a list — those stay
   instant, like Finder.
6. **Reduce Motion.** When macOS *Reduce motion* is on, the host window gets the
   `ReduceMotion` class (`MainWindowViewModel.ReduceMotion`, read via
   `MacAccessibility`). Scaling and sliding are switched off under
   `Window.ReduceMotion …` selectors; opacity fades remain.

**What's in place**

| Element | How | Values |
|---|---|---|
| Every `Button` | Press dip (`Button:pressed`) | `scale(0.97)`, 120ms `CubicEaseOut` |
| Small buttons (`IconBtn`, `DeleteBtn`, `Swatch`) | Deeper dip so it's visible | `scale(0.9)` |
| Full-width rows (`SidebarProfile`) and scrims (`Scrim`) | No dip — press fill only | — |
| Modal sheets (`Panel.SheetOverlay` + `behaviors:Sheet.IsOpen`) | Fade + grow from 96% toward the trigger, shrink back on close | opacity 200ms; scale spring R 0.35s |
| Content that appears (`Classes="Appear"`) | Fade in when it becomes visible | 180ms `CubicEaseOut`, opacity only |
| A newly opened tool (`ContentControl.Workspace`) | Fade in | 180ms, opacity only |
| Accent swatch ring (`Ellipse.SwatchRing`) | Grows in | spring R 0.30s |
| Segmented control (Background setting) | Selected capsule cross-fades | 150ms brush transition |
| Hover-revealed row buttons (`DeleteBtn`) | Fade in on row hover | 120ms |
| Flow Runs detail drawer | Slides in from the right edge and back out | spring R 0.35s; self-contained in the plugin |

**Springs in Avalonia.** `SpringEasing` runs in *normalised* time (0–1 of the
transition `Duration`), not seconds. To use Apple's *response* `R` (seconds) with
damping ratio 1.0 and a transition `Duration` `D`:

```
ωn        = 2π / R × D
Stiffness = ωn²         (Mass = 1)
Damping   = 2 × ωn
```

Pick `D ≈ 1.2–1.3 × R` so the spring has settled (< 0.5% left) before the
transition ends. Current values: R 0.35s / D 0.45s → `Stiffness 65, Damping 16.2`;
R 0.30s / D 0.35s → `Stiffness 54, Damping 14.7`.

**Motion in plugins.** Plugins inherit the classes above, but a plugin can be
installed on an *older* host that doesn't have them. So a plugin may only use
the shared motion classes as *optional polish* (they no-op on old hosts — e.g.
`Appear`). Anything a plugin's layout depends on — like an overlay that must
start hidden — must be self-contained in the plugin (see the Flow Runs
`Drawer` behavior and its local styles).

---

## 8. Platform constraints (hard-won — do not re-derive)

These cost real iteration. Respect them:

1. **Avalonia `BoxShadow` on buttons clips.** The Fluent `Button` template sets
   `ClipToBounds="True"`, and the content card clips at its rounded edge — both cut
   drop shadows. We abandoned button/input shadows entirely in favor of the
   `PillEdgeBrush` hairline. **Don't reintroduce drop shadows on buttons/inputs.**
   (We also dropped shadows on the big floating *cards*: on the lighter Theme/White
   backgrounds they read as a heavy halo. The cards separate via their margin and
   background contrast instead.)
2. **`AcrylicBlur` only frosts the desktop behind the window**, not in-app content
   behind a control. "Glass" buttons over white panels look muddy and were dropped.
   (Acrylic *is* still offered as the **Glass** window-background option — that
   frosts the desktop in the gaps around the cards, which works; glass *behind a
   control* does not.)
3. **Focus rings as `BoxShadow` render jagged corners** and thrash on caret blink.
   If you ever need a focus ring, use a border, not a shadow.
4. **`IsHitTestVisible="False"` disables the whole subtree** — children can't opt
   back in. Mark only the specific non-interactive element, not its container.
5. **CommunityToolkit MVVM:** a command's `CanExecute` only re-evaluates when a
   bound property fires `NotifyCanExecuteChangedFor`. Forgetting this leaves buttons
   permanently disabled — annotate every property the `CanExecute` reads.
6. **`RequestedThemeVariant="Light"`** is set on `Application`. The app is
   light-only; dark mode is not supported and styles assume light surfaces.
7. **Later styles win, and `Transitions` is a single property.** Avalonia has no
   CSS-style specificity: when two matching styles set the same property, the one
   later in `App.axaml` wins. A style that sets `Transitions` *replaces* the
   shared `Button` press transition — so list every transition the element needs
   (e.g. `Button.DeleteBtn` sets Opacity *and* RenderTransform) and place it after
   the shared rule.
8. **Disabling a control does not clear keyboard focus.** A sheet hidden with
   `Opacity=0` + `IsEnabled=False` can still hold focus and receive typing. Use
   `behaviors:Sheet.IsOpen`, which sets `IsVisible=False` once the fade-out
   finishes (that does clear focus).
9. **Give animated transforms an explicit resting value** (`scale(1)`,
   `translateX(0px)`), not an unset `RenderTransform`, so a transition always has
   a start and end value to interpolate between.
10. **Don't cross-fade plugin views with `TransitioningContentControl`.** It keeps
    the outgoing view in the tree during the fade; a plugin that returns a cached
    view from `CreateView()` would then be in two places at once and throw. Fade
    the incoming view only (`ContentControl.Workspace`).
11. **A full-bleed `Button` used as a scrim** inherits the grey hover/press pill
    fill. Give it the `Scrim` class and pin its hover/pressed background.

---

## 9. Checklist for new UI

- [ ] Uses tokens, no hard-coded hexes — `{DynamicResource}` for accent/background-following colours, `{StaticResource}` for fixed ones.
- [ ] Buttons use a class from §3; at most one `Primary` per group; red only for destructive.
- [ ] Inputs are white pills, radius 18, no hover/focus border.
- [ ] New surfaces follow §5 (white cards, no card shadow, `#F8F8FA` bars, hairlines `#E5E5EA`).
- [ ] No drop shadows on buttons/inputs or cards; no glass *behind controls*.
- [ ] Motion follows §7: modal overlays use `Panel.SheetOverlay` + `Sheet.IsOpen`; panes that appear use `Appear`; nothing decorative; works with Reduce Motion on.
- [ ] Titles 14–17pt carry the tracking from §6.
- [ ] Plugins: anything the layout depends on is self-contained (works on an older host).
- [ ] Destructive actions confirm before acting (see the publish/delete pattern).
- [ ] Any new `CanExecute` command has matching `NotifyCanExecuteChangedFor`.
- [ ] Verified by running the app, not just building (see `/verify`).
