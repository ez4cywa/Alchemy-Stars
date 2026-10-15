# Alchemy Stars design contract

Updated 2026-10-15. See [source and provenance](../../DESIGN.md). This contract adapts the pinned Linear reference to an Avalonia desktop editor; it is not a landing-page template.

## Product and layout

Alchemy Stars is a CAST animation and model workbench. Keep the asset library, central viewport, layer timeline, inspector, Model merger, dual-wield workspace and COD database. Favor flat adjoining panels with hairline dividers over floating or nested cards.

- Normal window: 1460 × 900 DIP. Supported minimum: 900 × 600 DIP.
- Sidebar: 172 DIP, compact mode: 56 DIP. Preserve collapsible groups and remembered state.
- Keep primary controls at least 44 DIP high. Density comes from hierarchy and spacing, not smaller hit targets.
- One primary export action, quieter supporting commands, visible selection and keyboard focus.
- Do not copy marketing heroes, pricing cards, promotional copy or website motion into this application.

## Semantic color tokens

Use the existing `Alchemy…Brush` resources, owned by `ModernDesktopAppearance`, rather than page-specific literals.

| Role | Light | Dark |
| --- | --- | --- |
| Canvas | #f6f7f8 | #010102 |
| Sidebar | #f6f7f8 | #101112 |
| Surface | #ffffff | #0f1011 |
| Raised surface | #f4f5f6 | #18191a |
| Primary text | #202125 | #f7f8f8 |
| Secondary text | #646970 | #a0a4ad |
| Decorative divider | #e3e5e8 | #23252a |
| Interactive boundary | #878b95 | #686c74 |
| Primary action | #5e6ad2 | #5e6ad2 |
| Accent text / focus | #535fbc | #9da7ff |
| Primary hover | #535fbc | #535fbc |
| Neutral selection | #e9eaed | #24262b |
| Error | #b3263b | #ff8d9b |
| Success | #237542 | #67ce8d |

Reserve indigo for primary actions, focus and meaningful active controls. Navigation and icons remain neutral. Decorative hairlines do not replace the stronger boundary needed to identify inputs. Aim for at least 4.5:1 normal-text contrast and 3:1 meaningful non-text contrast. Disabled controls are exempt but still identifiable.

## Typography, spacing and shape

- Font stack: Inter, Segoe UI, Microsoft YaHei UI, sans-serif. Do not bundle Linear's proprietary fonts.
- Body and controls: 13 DIP; secondary labels: 12; section titles: 15; page titles: 18.
- Normal letter spacing, including headings. Keep technical identifiers readable and selectable where appropriate.
- Spacing scale: 4, 8, 12, 16, 24 DIP.
- Controls: radius 6; dialogs: radius 8; adjoining panels: radius 0.
- No decorative shadows, gradients, ambient glows, spring motion or press scaling.
- Preserve imported theme radius overrides for both controls and their template presenters.

## Components and states

- Use the existing consistent vector icon family, with user-imported icon packs taking precedence. No emoji substitutions.
- Navigation: neutral hover and selection, readable label and icon; expose the current page without relying on color alone.
- Forms: aligned labels, complete text, visible input boundaries, stable 44-DIP controls.
- Settings: flat sections and dividers; do not nest framed cards just to group related fields.
- Dialogs: one quiet raised surface; preserve modal focus containment, error/success semantics and return focus.
- Explicit enabled, disabled, hovered, pressed, selected and keyboard-focus states. Do not animate layout geometry.
- Keep localized tooltips, automation names and existing shortcuts.

## Compatibility and ownership

The default style is displayed as **Linear · Workbench / Linear · 工作台**. Its persistent identifier remains `apple`; `modern-desktop` and `neumorphic` continue to normalize to that key. Do not migrate project files or add an unnecessary theme identifier.

Classic Apple, Windows XP, Windows 2000 and imported themes remain independent. Apply base tokens first, then custom overrides. Remove the previous control skin before adding the next so style frames cannot leak between themes. Appearance preferences remain separate from animation data.

## Finish gate

- Render and inspect all seven pages, a real CAST preview and centered dialogs.
- Verify light, dark and system modes; Chinese and English; 900 × 600 and normal desktop sizes.
- Switch themes in the running application; confirm the workspace is unchanged and no stale skin remains.
- Import, reject, reload and reset custom themes/icons; verify custom radii reach real template presenters.
- Exercise keyboard navigation, modal focus, toolbar clipping and export workflows.
- Run managed self-tests and the Native AOT verification suite. State skipped asset-dependent checks explicitly.
