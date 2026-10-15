# Alchemy Stars UI design

Use the [project design contract](design-system/alchemy-stars/MASTER.md) for Avalonia implementation. The default workspace follows the Linear design language selected by the user on 2026-10-15.

The installed reference library is `C:/Users/EZ4/.codex/design-references/awesome-design-md`. Portable, pinned source material is retained at [Linear DESIGN.md](design-system/references/awesome-design-md/linear.app/DESIGN.md).

## Source and adaptation

- Upstream: [VoltAgent/awesome-design-md](https://github.com/VoltAgent/awesome-design-md)
- Commit: `3b605329240ce735b510037cca322f1831fdd7f8`
- Source path: `design-md/linear.app/DESIGN.md`
- License: MIT; [original notice](design-system/references/awesome-design-md/LICENSE) retained alongside the reference.
- This is a design-reference collection, not an executable tool or agent skill. No runtime package or service is added to Alchemy Stars.

The source analyzes Linear's marketing website. Adapt its neutral hierarchy, scarce accent and fine dividers to a desktop editor; do not copy promotional heroes or pricing layouts. Keep real workspaces, keyboard behavior, light/dark/system modes and existing 44-DIP primary hit targets. Use system font fallbacks, normal letter spacing and compact desktop typography.

The saved `apple` style identifier and its aliases remain compatible. It now renders the Linear workbench; other skins and imported themes stay independent.
