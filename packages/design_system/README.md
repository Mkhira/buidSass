# Design System

Shared design tokens and theme definitions for Flutter and web consumers.

## RTL Mirroring Rules

- Use `EdgeInsetsDirectional` (not `EdgeInsets`) for paddings/margins tied to start/end.
- Use `AlignmentDirectional` (not `Alignment`) for horizontal positioning tied to start/end.
- Keep size tokens direction-agnostic; only layout application mirrors.
- Icons with directional meaning (back, forward, chevron, arrow) must use RTL-aware variants.
- Neutral glyphs (check, close, plus, menu) do not mirror.
