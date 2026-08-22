# Galaxy Buds for Omarchy

An Omarchy Quattro bar plugin for Galaxy Buds, backed by
[GalaxyBudsClient](https://github.com/timschneeb/GalaxyBudsClient).

The goal is a small, native Omarchy panel for the controls and status worth
having one click away:

- left, right, and case battery
- charging and wear state
- current noise-control mode
- noise control, equalizer, touch lock, and supported convenience toggles
- a shortcut to the full GalaxyBudsClient app for everything else

This repository is at the initial implementation stage. See
[`docs/exploration.md`](docs/exploration.md) for the integration findings and
MVP boundary.

## Proposed architecture

1. A GalaxyBudsClient C# hook publishes its already-decoded status as atomic
   JSON under `$XDG_STATE_HOME/omarchy-buds/`.
2. The Omarchy plugin watches that file with Quickshell `FileView`, so status
   updates do not require polling Bluetooth.
3. Controls use GalaxyBudsClient's supported D-Bus/CLI action surface instead
   of opening a second Bluetooth connection.

The plugin must never talk to the Buds protocol directly while
GalaxyBudsClient owns the RFCOMM connection.

## Requirements

- Omarchy Quattro
- GalaxyBudsClient 5.2 or newer
- Galaxy Buds paired in BlueZ

## Inspiration

The panel and repository shape are inspired by
[`thisisgm/omarchy-pods`](https://github.com/thisisgm/omarchy-pods), while the
integration strategy is specific to GalaxyBudsClient.
