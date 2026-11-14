# UI styles, animations and the status strip

This mod adds three looks for the whole app, a motion setting, and a new row at the top of the dashboard. Everything is in **Settings → Appearance**, except the dashboard row, which is in **Dashboard → Customize**.

## Styles

| Style | Look | Light/dark | Background |
|---|---|---|---|
| **Fluent glass** (default) | The app as it always was: WPF-UI's Windows 11 colors, your accent color, Mica/Acrylic backdrops and optional background image | Follows the Theme setting | Your backdrop and image settings |
| **Neon** | Dark graphite, cyan accent, color-coded power modes, a soft glow on active items, a light sweeping along the title bar | Dark only | Solid |
| **Retro CRT** | A monochrome terminal: green, amber or white phosphor, monospaced text, square corners, scanlines, a vignette, a slowly rolling band and a warm-up effect | Dark only | Solid |

Switching style applies immediately, no restart needed. When a dark-only style is active, the Theme picker is disabled with a note. The backdrop and background-image settings are kept for Fluent and show a note while a solid style is active.

**Windows High Contrast** always uses Fluent, whatever is selected, because WPF-UI's own colors are the ones that follow the system's high-contrast setup.

### Style options

| Option | Shown for | What it does |
|---|---|---|
| Glow (0–100%) | Neon | Strength of the glow on the active navigation item, the power-mode highlight and the status strip's power tile. 0% removes the effect entirely. |
| Phosphor (Green / Amber / White) | Retro | Screen color |
| Scanlines (0–100%) | Retro | Darkness of the scanline pattern |
| Phosphor glow (on/off) | Retro | A soft bloom around everything on screen (one effect on the window content) |

Glow and phosphor glow need the GPU to draw effects (render tier 2 with hardware acceleration on). Otherwise they're switched off automatically, and the settings card says so.

### Readability

Every style's text has at least **4.5:1** contrast (WCAG AA) on the window background and on cards. This is checked by unit tests in `LenovoLegionToolkit.Tests/UI/SkinPaletteTests.cs` for every style and every phosphor color. Power-mode colors reach at least 3:1 against the background, and Retro's inverse-video selection reaches at least 4.5:1.

One color differs from the original mockup: Retro amber's dim text is `#B8801F` instead of `#A8721A`, because the darker shade only reached 4.46:1 on cards.

## Animations

**Settings → Appearance → Animations** has three levels:

| Level | What moves |
|---|---|
| **Full** | Pages slide in (220 ms), dashboard groups fade and rise in one after another, cards lift slightly under the pointer, numbers count up, bars glide, and the style's looping effects run (Neon sweep; Retro band, flicker and cursor) |
| **Reduced** | Fades only (pages fade in 150 ms). Nothing moves, counts or loops. |
| **Off** | No animation |

Full is only used when all of these are true:

- Windows **Animation effects** is on (Settings → Accessibility → Visual effects);
- the app's **hardware acceleration** is on (Settings → Appearance; needs a restart);
- the GPU renders at WPF tier 2.

Otherwise the app uses Reduced, and the Animations card says why.

> Hardware acceleration has always been **off by default** in LLT, so out of the box you'll see Reduced. Turn hardware acceleration on and restart to get Full.

Looping effects run only while the main window is on screen. They stop when it's minimized or hidden in the tray, and start again when it comes back.

## Dashboard status strip and power-mode selector

A row at the top of the dashboard with five tiles:

| Tile | Big value | Underneath | Bar |
|---|---|---|---|
| Power mode | Current mode, with its color | Adapter: plugged in, low-wattage adapter, or on battery | Mode color |
| CPU | Temperature | Load · clock | Load |
| GPU | Temperature | Load · clock | Load |
| Fans | CPU fan speed | GPU fan speed | CPU fan vs. its maximum |
| Battery | Charge | AC connected / disconnected | Charge |

Below it, a **power-mode selector** has one button per mode your laptop supports. A highlight slides to the active mode.

How it behaves:

- **Real data only.** Values come from the same sources as the sensors section and the power-mode card. A reading the hardware doesn't report (for example a sleeping discrete GPU) shows **—** and an empty bar, never an estimate.
- **Same refresh rate** as the sensors section you use. Reading stops while the strip isn't visible.
- **Same switching** as the power-mode card: the same "needs the charger" message, and the same settings button for Balance (AI mode) and Custom (God Mode), including the one-time God Mode warning. The card and the selector stay in sync, also with Fn+Q.
- **Neon:** the CPU temperature turns orange above 78 °C.
- **Retro:** bars are drawn as block characters (`█████░░░░░`), modes are labelled `[ PERFORMANCE ]`, the selection is drawn in inverse video, and a `C:\LLT>` line types out the last change, for example `POWER MODE = PERFORMANCE. OK`. A failed change says `FAILED`.

Turn the row on or off in **Dashboard → Customize → Status strip and power mode selector**.

## For developers

| Piece | Where |
|---|---|
| Palettes (colors, fonts, glow, mode colors) | `LenovoLegionToolkit.WPF/Utils/Skins/SkinPalette.cs` |
| Applying a style at runtime | `LenovoLegionToolkit.WPF/Utils/ThemeManager.cs` |
| Glow effect | `LenovoLegionToolkit.WPF/Utils/Skins/SkinGlow.cs` |
| Retro text helpers | `LenovoLegionToolkit.WPF/Utils/Skins/RetroText.cs` |
| Contrast math | `LenovoLegionToolkit.WPF/Utils/Skins/ColorContrast.cs` |
| Motion level and animation helpers | `LenovoLegionToolkit.WPF/Utils/Motion.cs` |
| Status strip | `LenovoLegionToolkit.WPF/Controls/Dashboard/StatusStripControl.*`, `StatusStripFormat.cs` |
| Power-mode selector | `LenovoLegionToolkit.WPF/Controls/Dashboard/PowerModeSelectorControl.*` |
| Power-mode logic shared with the card | `LenovoLegionToolkit.WPF/Controls/Dashboard/PowerModeActions.cs` |
| CRT layer and Neon sweep | `LenovoLegionToolkit.WPF/Windows/MainWindow.xaml(.cs)` |

**How styles work.** The app and WPF-UI's control templates read their colors from WPF-UI's named resources (`TextFillColorPrimaryBrush`, `CardBackgroundFillColorDefaultBrush`, …) through `DynamicResource`. A style is a resource dictionary that overrides those keys, merged after WPF-UI's theme dictionary, so the whole app recolors live.

- WPF-UI writes accent colors straight into the application's own resources, where they beat any merged dictionary, so a style's accent goes through `Accent.Apply` instead.
- Fluent adds no overrides at all.
- A test checks that every overridden key really exists in WPF-UI, so a misspelt key can't silently do nothing.

**Adding a style:**

1. Add a value to `UiSkin` (in `LenovoLegionToolkit.Lib/Enums.cs`) and its display name.
2. Add a factory in `SkinPalette` that returns a palette with `Colors`.
3. Add the style to `SkinPalette.Available`.

The existing theory tests then check its keys and contrast automatically.

## Not yet verified

These need a person and the real laptop:

- **How each style looks:** compare against the mockups.
- **CPU cost:** in Task Manager, compare each style with the window visible and with it minimized. Expect about 0% when minimized. The status strip reads the sensors in addition to the sensors section.
- **On the laptop:** real temperatures, fans and battery in the strip; the selector switching modes (including God Mode); Fn+Q updating both.

Retro uses **Consolas** (falling back to Lucida Console, then Courier New), which ships with Windows. The mockup's pixel font, VT323 (SIL Open Font License), isn't bundled yet.
