# Galaxy Buds for Omarchy

An Omarchy Quattro bar plugin for Galaxy Buds. It shows per-earbud status and
runs supported controls through
[GalaxyBudsClient](https://github.com/timschneeb/GalaxyBudsClient).

GalaxyBudsClient remains the only owner of the Buds protocol connection. This
plugin does not open Bluetooth or RFCOMM itself.

## What the MVP includes

- left, right, and case battery when GalaxyBudsClient reports them
- charging and wear placement hints
- the current Off, Noise cancellation, Ambient sound, or Adaptive mode
- GalaxyBudsClient toggle actions for ANC and Ambient sound
- equalizer, touch lock, conversation detection, and one-earbud noise control
  when the connected model and decoded status support them
- a shortcut to the full GalaxyBudsClient window

Adaptive is displayed when reported, but it is not an explicit control.
GalaxyBudsClient 5.2.1 does not expose an Adaptive setter through its public
action interface.

Bluetooth pairing, connection management, output selection, volume, and
microphone controls remain in Omarchy's stock Bluetooth and Audio panels.
Firmware updates, fit tests, gesture mapping, and detailed tuning remain in
GalaxyBudsClient.

## Requirements

- Omarchy Quattro
- GalaxyBudsClient 5.2.1 or newer available as `galaxybudsclient`
- Galaxy Buds already paired through BlueZ
- `busctl`, supplied by the systemd package used by Omarchy

The first version was checked against Omarchy 4.0 development packages,
Quickshell 0.3.0, and GalaxyBudsClient 5.2.1.

## Install

Clone the plugin through Omarchy without enabling it yet:

```bash
omarchy plugin add https://github.com/dgalarza/omarchy-buds
```

Install the status hook:

```bash
~/.config/omarchy/plugins/io.github.dgalarza.omarchy-buds/setup
```

`setup` only copies `galaxy-client/OmarchyBudsStatus.cs` into the active
GalaxyBudsClient script directory. It checks the current
`$XDG_DATA_HOME/GalaxyBudsClient/scripts` layout and the older
`$XDG_CONFIG_HOME/GalaxyBudsClient/scripts` layout. It does not install or
restart GalaxyBudsClient, edit Omarchy settings, or enable the widget.

Fully quit and relaunch GalaxyBudsClient once so its script manager loads the
hook. Then enable the widget:

```bash
omarchy plugin enable io.github.dgalarza.omarchy-buds
```

The icon is hidden while disconnected by default. To leave it visible for
setup and connection diagnostics:

```bash
omarchy bar set io.github.dgalarza.omarchy-buds hideWhenDisconnected false --json
```

## How it works

The C# hook subscribes to GalaxyBudsClient's decoded status events. It writes a
schema-versioned snapshot atomically to:

```text
$XDG_STATE_HOME/omarchy-buds/status.json
# fallback: ~/.local/state/omarchy-buds/status.json
```

`Service.qml` watches that file with Quickshell `FileView`. It also checks that
GalaxyBudsClient still owns `me.timschneeberger.GalaxyBudsClient` on the user
bus and matches the bus owner's process ID to the snapshot writer. A snapshot
left behind by a crash therefore cannot be presented as a live connection.

Controls execute known `galaxybudsclient action -e ...` identifiers. The panel
accepts only the identifiers its parser recognizes and only renders an action
after the hook has observed a compatible device capability and an extended
status update.

The integration boundary is limited to `Service.qml` and
`galaxy-client/OmarchyBudsStatus.cs`. Protocol decoding remains upstream.

## Controls

A left click opens the panel. The switches invoke GalaxyBudsClient toggle
actions and wait for the next decoded snapshot rather than claiming an
optimistic state.

| Key | Action |
|---|---|
| `j` / `k`, `↓` / `↑` | Move between available rows |
| `Enter` / `Space` | Activate the selected row |
| `n` | Toggle noise cancellation |
| `a` | Toggle ambient sound |
| `e` | Toggle the equalizer |
| `l` | Toggle touch lock |
| `c` | Toggle conversation detection |
| `o` | Toggle one-earbud noise control |
| `g` | Open GalaxyBudsClient |
| `r` | Reload status and client liveness |
| `Tab` / `Shift+Tab` | Move between bar panels |
| `Esc` | Close the panel |

Keys for unsupported or unavailable actions do nothing. There is no Adaptive
shortcut.

## Troubleshooting

**The icon is absent**

The default setting hides it while disconnected. Temporarily disable
`hideWhenDisconnected` with the command above. The open panel distinguishes a
stopped client, a missing hook, malformed status, and a disconnected device.

**The panel says the hook is not loaded**

Run `setup`, then fully quit and relaunch GalaxyBudsClient. Copying a C# script
while the client is running does not load it.

**The status file never appears**

Check GalaxyBudsClient's `application.log` in its data directory for
`OmarchyBudsStatus` or CSScript compiler errors. GalaxyBudsClient's scripting
API is the least stable part of this integration and may require an update to
the hook after a client release.

**A switch does not move immediately**

The panel deliberately waits for the state GalaxyBudsClient decodes from the
earbuds. A CLI failure is shown in the panel; a successful action can still
take a moment to produce a new status packet.

## Remove

Disable and remove the Omarchy plugin first:

```bash
omarchy plugin disable io.github.dgalarza.omarchy-buds
omarchy plugin remove io.github.dgalarza.omarchy-buds
```

The hook is installed outside the plugin directory, so remove the copy from
the layout GalaxyBudsClient uses:

```bash
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/GalaxyBudsClient/scripts/OmarchyBudsStatus.cs"
rm -f "${XDG_CONFIG_HOME:-$HOME/.config}/GalaxyBudsClient/scripts/OmarchyBudsStatus.cs"
rm -rf "${XDG_STATE_HOME:-$HOME/.local/state}/omarchy-buds"
```

Restart GalaxyBudsClient after removing the hook.

## Development checks

No check requires installing or enabling the plugin:

```bash
omarchy plugin validate .
deno run --allow-read tests/model.test.js
bash -n setup
qmllint -I /usr/share/omarchy/shell Panel.qml Service.qml GalaxyBudsIcon.qml
```

`tests/model.test.js` covers complete, partial, absent, malformed, and
unsupported-version status input. See [`docs/exploration.md`](docs/exploration.md)
for the verified GalaxyBudsClient and Omarchy integration surfaces.

## License

MIT. See [`LICENSE`](LICENSE).
