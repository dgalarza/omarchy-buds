# Agent brief

Read `docs/exploration.md` before changing code.

## Goal

Build a focused first working version of the Omarchy Quattro bar plugin
described in the exploration notes.

## Constraints

- Plugin ID: `io.github.dgalarza.omarchy-buds`.
- GalaxyBudsClient remains the only owner of the Buds protocol connection.
- Do not copy code or artwork from omarchy-pods. Its architecture and panel
  idiom are references, not source material for this repository.
- Do not modify `~/.config/omarchy`, install the plugin, run `setup`, restart
  GalaxyBudsClient, or send control actions to the connected earbuds while
  implementing.
- Do not edit `/usr/share/omarchy`.
- Do not add a second Bluetooth/RFCOMM implementation.
- Keep protocol/client integration in a small, replaceable boundary.
- The C# hook output must be versioned JSON and written atomically.
- Parse status in pure `Model.js` functions and cover malformed, absent, and
  partial input with Deno tests.
- Render only controls backed by an observed GalaxyBudsClient capability or
  action. Do not claim Adaptive can be set explicitly unless verified.
- Prefer current Omarchy `Panel`, `KeyboardPanel`, `PanelHero`,
  `PanelSectionHeader`, `CursorSurface`, and `PanelKeyCatcher` patterns.
- Use plain technical prose. Avoid generated-attribution commit trailers.

## Local references

Read-only source trees prepared during exploration:

- `/tmp/omarchy-pods-explore`
- `/tmp/GalaxyBudsClient-explore`
- `/usr/share/omarchy/shell/plugins/panels/bluetooth`
- `/usr/share/omarchy/shell/plugins/panels/audio`

## Validation

At minimum run:

```bash
omarchy plugin validate .
deno run --allow-read tests/model.test.js
```

Run any additional static QML or script checks available without installing
or enabling the plugin. Commit the implementation when the checks pass.
