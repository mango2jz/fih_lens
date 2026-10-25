# FIH Lens — Claude Code

Follow the repository's agent instructions:

@AGENTS.md

## UI redesign (Spotlight)

The WinUI front end is being rebuilt to the Spotlight design.

- Spec: `docs/design/spotlight/README.md`. Read it before any UI work.
- Plan and phases: `docs/plans/spotlight-redesign.md`. Do one phase at a time and stop after it for a manual check.
- Colours, type, spacing, radii and shadows come only from `docs/design/spotlight/tokens.json`, mapped into `FihLens.WinUI/Resources/Phase1Tokens.xaml`. No hard-coded colours in views.
- Exact sizes and paddings are in `docs/design/spotlight/reference-css/components.css`. Port the values, not the CSS.
- To check a screen, compare it against `docs/design/spotlight/screens/<name>-dark.png` and `<name>-light.png`.
