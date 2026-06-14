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

---

## 7. Iconography & motion

- **Icons:** simple Unicode glyphs (⚙ ✎ ✕ ↑ ＋) at low-key greys, not an icon font.
  Web-resource type badges are 3-letter labels on a colored rounded rect
  (`WebResourceItem.TypeColor`).
- **App icon:** generated by `scripts/create-icon.py` -> a dotted "global data"
  globe on a blue squircle, with the wordmark "VERSE" spaced wide across the centre
  so it reads as the equator line. Re-run that + `generate-icon.sh` to change it.
- **Motion:** essentially none. We tried an animated connection glow and removed it.
  Prefer static state changes. If you add motion, keep it sub-300ms and optional.

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

---

## 9. Checklist for new UI

- [ ] Uses tokens, no hard-coded hexes — `{DynamicResource}` for accent/background-following colours, `{StaticResource}` for fixed ones.
- [ ] Buttons use a class from §3; at most one `Primary` per group; red only for destructive.
- [ ] Inputs are white pills, radius 18, no hover/focus border.
- [ ] New surfaces follow §5 (white cards, no card shadow, `#F8F8FA` bars, hairlines `#E5E5EA`).
- [ ] No drop shadows on buttons/inputs or cards; no glass *behind controls*; minimal/no motion.
- [ ] Destructive actions confirm before acting (see the publish/delete pattern).
- [ ] Any new `CanExecute` command has matching `NotifyCanExecuteChangedFor`.
- [ ] Verified by running the app, not just building (see `/verify`).
