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

Defined in `App.axaml` → `Application.Resources`. Reference them with
`{StaticResource Key}`; never hard-code these hexes in a view.

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

### Legacy / deprecated tokens (do not use in new work)

These are leftovers from abandoned experiments (colored buttons, glass, filled
fields). They remain only so nothing breaks; **prefer the active tokens** and feel
free to delete these in a cleanup pass:
`TintBrush`, `TintHoverBrush`, `TintPressedBrush`, `AccentTextBrush`,
`SuccessTintBrush`/`*Hover`/`*Pressed`, `SuccessTextBrush`,
`AccentHoverBrush`, `AccentPressedBrush`, `FieldBorderBrush`,
`FieldFillBrush`, `FieldFillHoverBrush`, `ControlHoverBrush`, `ControlPressedBrush`.

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

- **Window:** `TransparencyLevelHint="AcrylicBlur"`, transparent background,
  extended client area. The blurred desktop shows only in the gaps *around* the
  floating panels — see the acrylic constraint in §8.
- **Title bar:** 52px row. The grid stays hit-testable; only the centered title is
  `IsHitTestVisible="False"` so the window still drags but the cog stays clickable.
- **Floating cards:** solid white, `CardRadius` (14), `BoxShadow="0 4 24 4 #28000000"`,
  with a margin so the acrylic shows around them. This is the sidebar and the
  content area. Card shadows are fine — they sit over the desktop, not over
  in-app content.
- **Bars** (toolbars, action bars): `#F8F8FA` fill, `#E5E5EA` hairline on the
  dividing edge, compact padding (`16,10` top bar, `12,8` action bar).
- **Modal sheets** (`Border.FormCard`): white, radius `16`, `BoxShadow="0 6 20 2 #2E000000"`,
  centered, **no dim backdrop** (the card shadow alone separates it). A ✕ in the
  top-right is the dismiss control — do **not** add a duplicate Cancel button.
- **Status chip** (`Border.StatusChip`): neutral grey; `.connected` class swaps to a
  solid pale-green fill (`#E3F5E9`). No animation.
- **Sidebar list** (`ListBox.SidebarList`): selection is a pale tint
  (`#E9F2FF`) with dark text, Finder-style — not a saturated fill.

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
- **App icon:** generated by `scripts/create-icon.py` -> bold "VK" lettermark with a
  faint vertical gradient on a blue squircle (App Store style). Re-run that +
  `generate-icon.sh` to change it.
- **Motion:** essentially none. We tried an animated connection glow and removed it.
  Prefer static state changes. If you add motion, keep it sub-300ms and optional.

---

## 8. Platform constraints (hard-won — do not re-derive)

These cost real iteration. Respect them:

1. **Avalonia `BoxShadow` on buttons clips.** The Fluent `Button` template sets
   `ClipToBounds="True"`, and the content card clips at its rounded edge — both cut
   drop shadows. We abandoned button/input shadows entirely in favor of the
   `PillEdgeBrush` hairline. **Don't reintroduce drop shadows on buttons/inputs.**
   (Shadows on the big floating *cards* are fine — they overflow into the acrylic gap.)
2. **`AcrylicBlur` only frosts the desktop behind the window**, not in-app content
   behind a control. "Glass" buttons over white panels look muddy and were dropped.
   Real glass only works over the transparent title-bar region, which isn't worth a
   single control.
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

- [ ] Uses `{StaticResource}` tokens, no hard-coded hexes for themed colors.
- [ ] Buttons use a class from §3; at most one `Primary` per group; red only for destructive.
- [ ] Inputs are white pills, radius 18, no hover/focus border.
- [ ] New surfaces follow §5 (white cards on acrylic, `#F8F8FA` bars, hairlines `#E5E5EA`).
- [ ] No drop shadows on buttons/inputs; no glass; minimal/no motion.
- [ ] Destructive actions confirm before acting (see the publish/delete pattern).
- [ ] Any new `CanExecute` command has matching `NotifyCanExecuteChangedFor`.
- [ ] Verified by running the app, not just building (see `/verify`).
