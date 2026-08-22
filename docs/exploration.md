# Integration exploration

Verified on 2026-08-22 with Omarchy `4.0.0.r1791.ged7bae4-1`, Quickshell
`0.3.0`, and GalaxyBudsClient `5.2.1`.

## What is already available

The installed client is `/usr/bin/galaxybudsclient` from the AUR package
`galaxybudsclient-bin`. The paired device used for exploration reports as:

- name: `Damian's Buds4 Pro`
- model: `Buds4Pro`
- address: `8C:A3:EC:12:F2:54`

BlueZ exposes connection state and one aggregate `org.bluez.Battery1`
percentage. In the observed session that value was 91%, while
GalaxyBudsClient reported left 95% and right 91%. BlueZ alone is therefore not
enough for the intended panel.

## GalaxyBudsClient's public integration surface

While its desktop process is running, GalaxyBudsClient owns this session-bus
name:

```text
me.timschneeberger.GalaxyBudsClient
```

The application object is:

```text
/me/timschneeberger/galaxybudsclient
me.timschneeberger.GalaxyBudsClient.Application
```

It exposes `ListActions`, `ExecuteAction`, `Activate`, and
`ShowBatteryPopup`. The installed CLI wraps those calls:

```bash
galaxybudsclient action -l
galaxybudsclient action -e AncToggle
galaxybudsclient app --activate-window
```

Useful action identifiers observed in 5.2.1 include:

- `AmbientToggle`
- `AmbientVolumeUp` / `AmbientVolumeDown`
- `AncToggle`
- `SwitchAncSensitivity`
- `SwitchAncOne`
- `EqualizerToggle` / `EqualizerNextPreset`
- `LockTouchpadToggle`
- `ToggleConversationDetect`
- `ToggleDoubleEdgeTouch`
- `ShowBatteryPopup`

The device object appears only when GalaxyBudsClient has its protocol
connection open:

```text
/me/timschneeberger/galaxybudsclient/device
me.timschneeberger.GalaxyBudsClient.Device
```

`galaxybudsclient device -G -j` returned these live properties:

```json
{
  "Name": "Damian's Buds4 Pro",
  "Address": "8C:A3:EC:12:F2:54",
  "Model": "Buds4Pro",
  "BatteryLeft": 95,
  "BatteryRight": 91,
  "BatteryCase": 0,
  "WearStateLeft": "Wearing",
  "WearStateRight": "Wearing",
  "FirmwareVersion": "R640XXU0AZD2",
  "HardwareVersion": "rev0.2"
}
```

These properties emit `org.freedesktop.DBus.Properties.PropertiesChanged`.
The D-Bus object does not expose charging state, current noise mode,
equalizer state, touch lock, or conversation detection.

## Why the scripting hook is the best status bridge

GalaxyBudsClient loads C# hooks at startup through `CSScriptLib`. In the
installed 5.2.1 build, the runtime log reports this directory:

```text
~/.local/share/GalaxyBudsClient/scripts
```

The repository's older script README still says
`~/.config/GalaxyBudsClient/scripts`, so setup should detect the active data
layout rather than copying blindly.

A hook can subscribe to the client's public decoded-message events. In
particular, `ExtendedStatusUpdateDecoder` already contains the complete state
needed by the panel:

- `BatteryL`, `BatteryR`, and `BatteryCase`
- `IsLeftCharging`, `IsRightCharging`, and `IsCaseCharging`
- `PlacementL` and `PlacementR`
- `NoiseControlMode`: Off 0, ANC 1, Ambient 2, Adaptive 3
- `EqualizerMode`
- `TouchpadLock`
- `NoiseControlsWithOneEarbud`
- `DetectConversations`
- ambient level and other model-dependent fields

Source verification also found device-response events for control changes.
Modern noise-control devices return `NOISE_CONTROLS` acknowledgements or
`NoiseControlUpdateResponse`; legacy ANC and Ambient changes have dedicated
update responses. Equalizer, touch lock, conversation detection, and
one-earbud noise control are represented in the universal acknowledgement
decoder. The hook can therefore publish returned device values rather than
inverting state when a local action is dispatched.

The hook should write a versioned JSON snapshot atomically to:

```text
$XDG_STATE_HOME/omarchy-buds/status.json
# fallback: ~/.local/state/omarchy-buds/status.json
```

The Quickshell side can use `FileView { watchChanges: true }`, matching the
successful event-driven pattern in omarchy-pods. It should also make stale
state impossible to mistake for a running client by handling disconnect and
process exit, with a lightweight D-Bus liveness fallback if needed.

## Why not connect to the Buds directly

GalaxyBudsClient's own warning is that only one application can interact with
the earbuds at a time. A second RFCOMM implementation would compete with the
installed client, duplicate its model support, and turn this plugin into a
protocol fork. The panel should consume the client's decoded state and send
commands through its action surface.

## Why not use the battery history database

GalaxyBudsClient stores history at paths such as:

```text
~/.local/share/GalaxyBudsClient/battery_stats_8CA3EC12F254.db
```

Its schema includes battery, charging, placement, and `NoiseControlMode`, but
it is historical data rather than an IPC contract. Records are deduplicated,
can lag the live decoder, and null frames are inserted around lifecycle
changes. It is useful for diagnostics, not as the panel's primary state.

## Omarchy plugin shape

Omarchy Quattro discovers a repository-root `manifest.json` with a
`bar-widget` entry point. The intended ID is:

```text
io.github.dgalarza.omarchy-buds
```

The initial repository should follow the current shell panel idiom:

```text
manifest.json
Panel.qml
Service.qml
Model.js
GalaxyBudsIcon.qml
setup
galaxy-client/OmarchyBudsStatus.cs
tests/model.test.js
```

The setup script should install only the C# hook, validate that
GalaxyBudsClient exists, and clearly ask for a GalaxyBudsClient restart. It
must not install or replace GalaxyBudsClient itself.

## MVP panel

Show:

- a Buds icon, hidden when disconnected by default
- model/device name
- left, right, and case battery with charging and wear hints
- selected Off, ANC, Ambient, or Adaptive mode when reported
- controls backed by supported GalaxyBudsClient actions
- equalizer, touch lock, conversation detection, and one-earbud noise control
  only when capability/state data makes them meaningful
- an action to open the full client

Leave to stock Omarchy panels:

- Bluetooth pairing, connect, disconnect, and forget
- output selection and volume
- microphone selection

Leave to GalaxyBudsClient:

- firmware updates
- fit test and find-my-buds flows
- detailed ambient tuning
- touch gesture mapping
- experimental or model-specific settings

## Known constraint

GalaxyBudsClient 5.2.1 exposes toggle actions rather than explicit setters,
and its public D-Bus device properties are intentionally small. The status
hook makes toggles safe to render because the panel can show the state that
comes back, but explicit mode selection must be tested against each action's
actual behavior. Do not invent an Adaptive setter when the client does not
provide one.

## Source references

- Omarchy panel reference:
  <https://github.com/thisisgm/omarchy-pods>
- GalaxyBudsClient:
  <https://github.com/timschneeb/GalaxyBudsClient>
- Relevant upstream files:
  `Cli/Ipc/Objects/DeviceObject.cs`, `Cli/Ipc/Objects/ApplicationObject.cs`,
  `Message/Decoder/ExtendedStatusUpdateDecoder.cs`, and
  `Scripting/ScriptManager.cs`
