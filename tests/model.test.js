// Run with: deno run --allow-read tests/model.test.js
// Model.js has no exports because it is also loaded as a QML JavaScript file.

const source = Deno.readTextFileSync(new URL("../Model.js", import.meta.url))
const Model = new Function(source + `
  return {
    parseStatus, batteryFrom, defaultBattery, defaultStatus, busctlProcessId,
    snapshotIsCurrent, snapshotNeedsProbe,
    noiseModeName, modelName, equalizerPresetName,
    levelText, levelFraction, batteryMeta, hasAnyBattery, elideError,
    SUPPORTED_SCHEMA, LEVEL_UNKNOWN, NOISE_UNKNOWN, NOISE_OFF, NOISE_ANC,
    NOISE_AMBIENT, NOISE_ADAPTIVE, ACTION_ANC_TOGGLE,
    ACTION_AMBIENT_TOGGLE, ACTION_EQUALIZER_TOGGLE,
    ACTION_TOUCH_LOCK_TOGGLE, ACTION_CONVERSATION_TOGGLE,
    ACTION_ONE_EARBUD_TOGGLE, MAX_ERROR_CHARS
  }
`)()

let failures = 0

function check(name, actual, expected) {
  const passed = JSON.stringify(actual) === JSON.stringify(expected)
  if (!passed) {
    failures++
    console.error(`FAIL ${name}\n  expected ${JSON.stringify(expected)}\n  got      ${JSON.stringify(actual)}`)
  }
}

function truthy(name, value) {
  check(name, Boolean(value), true)
}

const liveLine = JSON.stringify({
  schema_version: 1,
  written_at: "2026-08-22T18:43:12.345Z",
  process_id: 4242,
  connected: true,
  device_name: "Damian's Buds4 Pro",
  model: "Buds4Pro",
  address: "8C:A3:EC:12:F2:54",
  battery: {
    left: { available: true, level: 95, charging: true, placement: "Charging" },
    right: { available: true, level: 91, charging: false, placement: "Wearing" },
    case: { available: false, level: -1, charging: false, placement: "" }
  },
  capabilities: { case_battery: true },
  noise_control: { mode: 3 },
  equalizer: { enabled: true, preset: 2 },
  touch_lock: { enabled: false },
  conversation_detection: { enabled: true },
  one_earbud_noise_control: { enabled: true },
  actions: {
    anc_toggle: "AncToggle",
    ambient_toggle: "AmbientToggle",
    equalizer_toggle: "EqualizerToggle",
    touch_lock_toggle: "LockTouchpadToggle",
    conversation_detection_toggle: "ToggleConversationDetect",
    one_earbud_noise_control_toggle: "SwitchAncOne"
  }
})

const live = Model.parseStatus(liveLine)
check("live snapshot parses", live.ok, true)
check("live schema version", live.schemaVersion, 1)
check("writer process ID parses", live.processId, 4242)
check("device name is preserved", live.deviceName, "Damian's Buds4 Pro")
check("model identifier is preserved", live.model, "Buds4Pro")
check("left battery parses", live.left, {
  available: true,
  level: 95,
  charging: true,
  placement: "Charging"
})
check("an unavailable case discards its sentinels", live.caseBattery, Model.defaultBattery())
check("case capability is independent of current case availability", live.supportsCaseBattery, true)
check("Adaptive is accepted as observed status", live.noiseMode, Model.NOISE_ADAPTIVE)
check("equalizer state parses", [live.equalizerEnabled, live.equalizerPreset], [true, 2])
check("touch-lock state parses", live.touchLocked, false)
check("conversation state parses", live.conversationDetection, true)
check("one-earbud state parses", live.oneEarbudNoiseControl, true)
check("all confirmed control values parse", [
  live.noiseMode,
  live.equalizerEnabled,
  live.equalizerPreset,
  live.touchLocked,
  live.conversationDetection,
  live.oneEarbudNoiseControl
], [Model.NOISE_ADAPTIVE, true, 2, false, true, true])
check("all known actions are exposed", live.actions, {
  ancToggle: Model.ACTION_ANC_TOGGLE,
  ambientToggle: Model.ACTION_AMBIENT_TOGGLE,
  equalizerToggle: Model.ACTION_EQUALIZER_TOGGLE,
  touchLockToggle: Model.ACTION_TOUCH_LOCK_TOGGLE,
  conversationToggle: Model.ACTION_CONVERSATION_TOGGLE,
  oneEarbudToggle: Model.ACTION_ONE_EARBUD_TOGGLE
})

const controlsOff = Model.parseStatus(JSON.stringify({
  schema_version: 1,
  noise_control: { mode: 0 },
  equalizer: { enabled: false, preset: 2 },
  touch_lock: { enabled: false },
  conversation_detection: { enabled: false },
  one_earbud_noise_control: { enabled: false }
}))
check("confirmed false control values are preserved", [
  controlsOff.noiseMode,
  controlsOff.equalizerEnabled,
  controlsOff.equalizerPreset,
  controlsOff.touchLocked,
  controlsOff.conversationDetection,
  controlsOff.oneEarbudNoiseControl
], [Model.NOISE_OFF, false, 2, false, false, false])

// A valid partial snapshot is expected during connection setup, before the
// first extended-status packet arrives.
const partial = Model.parseStatus('{"schema_version":1,"connected":true,"device_name":"Buds"}')
check("partial snapshot parses", partial.ok, true)
check("partial snapshot defaults left battery", partial.left, Model.defaultBattery())
check("partial snapshot defaults mode", partial.noiseMode, Model.NOISE_UNKNOWN)
check("partial snapshot defaults actions", partial.actions, Model.defaultStatus().actions)
check("partial snapshot has no writer process", partial.processId, -1)
check("partial snapshot does not invent a model", partial.model, "")

const disconnected = Model.parseStatus('{"schema_version":1,"connected":false}')
check("disconnected snapshot parses", disconnected.ok, true)
check("disconnected flag remains false", disconnected.connected, false)
check("disconnected snapshot has no battery", Model.hasAnyBattery(disconnected), false)

// Every malformed or absent input returns the complete default shape and does
// not throw. File absence itself is handled by Service.qml as a load failure.
for (const [name, input] of [
  ["empty", ""],
  ["undefined", undefined],
  ["null value", null],
  ["garbage", "not JSON"],
  ["JSON null", "null"],
  ["JSON array", "[]"],
  ["missing schema", '{"connected":true}']
]) {
  const result = Model.parseStatus(input)
  check(`${name} input is rejected`, result.ok, false)
  truthy(`${name} input carries an error`, result.lastError)
  check(`${name} input still has a left battery object`, result.left, Model.defaultBattery())
}

const tooNew = Model.parseStatus('{"schema_version":2,"connected":true}')
check("newer schema is rejected", tooNew.ok, false)
check("newer schema is flagged", tooNew.schemaTooNew, true)
check("newer schema names both versions", tooNew.lastError,
  "The status hook writes schema 2; this panel reads 1")

const tooOld = Model.parseStatus('{"schema_version":0,"connected":true}')
check("older schema is rejected", tooOld.ok, false)
check("older schema is not marked too new", tooOld.schemaTooNew, false)

const stringSchema = Model.parseStatus('{"schema_version":"1","connected":true}')
check("string schema is rejected", stringSchema.ok, false)

const malformedFields = Model.parseStatus(JSON.stringify({
  schema_version: 1,
  connected: "yes",
  device_name: { text: "not a name" },
  model: 13,
  battery: {
    left: { available: true, level: 140, charging: true, placement: "Wearing" },
    right: { available: true, level: "91", charging: true, placement: ["Wearing"] },
    case: "invalid"
  },
  capabilities: { case_battery: "true" },
  noise_control: { mode: 99 },
  equalizer: { enabled: 1, preset: 8 },
  actions: {
    anc_toggle: "AncToggle; rm -rf /",
    ambient_toggle: "AncToggle",
    equalizer_toggle: 12
  }
}))
check("malformed fields do not reject a versioned snapshot", malformedFields.ok, true)
check("non-boolean connected is false", malformedFields.connected, false)
check("non-string identity is dropped", [malformedFields.deviceName, malformedFields.model], ["", ""])
check("out-of-range battery becomes unavailable", malformedFields.left, Model.defaultBattery())
check("numeric string battery becomes unavailable", malformedFields.right, Model.defaultBattery())
check("invalid mode becomes unknown", malformedFields.noiseMode, Model.NOISE_UNKNOWN)
check("invalid preset becomes unknown", malformedFields.equalizerPreset, -1)
check("invalid capabilities stay false", malformedFields.supportsCaseBattery, false)
check("unrecognized actions are dropped", malformedFields.actions, Model.defaultStatus().actions)

check("busctl process ID parses", Model.busctlProcessId("PID=4242\nUID=1000\n"), 4242)
check("busctl output must start a PID line", Model.busctlProcessId("PPID=42\nUID=1000\n"), -1)
check("busctl process ID must be positive", Model.busctlProcessId("PID=0\n"), -1)
check("busctl failure output has no process ID", Model.busctlProcessId("Failed to get credentials"), -1)
check("matching bus owner makes a snapshot current", Model.snapshotIsCurrent(live, 4242), true)
check("different bus owner makes a snapshot stale", Model.snapshotIsCurrent(live, 4243), false)
check("missing bus owner makes a snapshot stale", Model.snapshotIsCurrent(live, -1), false)
check("PID mismatch requests an immediate probe", Model.snapshotNeedsProbe(live, 4243), true)
check("missing owner requests an immediate probe", Model.snapshotNeedsProbe(live, -1), true)
check("matching owner needs no extra probe", Model.snapshotNeedsProbe(live, 4242), false)
check("malformed status does not drive probes", Model.snapshotNeedsProbe(Model.defaultStatus(), -1), false)

check("zero is a valid parsed battery level", Model.batteryFrom({
  available: true, level: 0, charging: false, placement: "Idle"
}).level, 0)
check("available false discards stale battery fields", Model.batteryFrom({
  available: false, level: 82, charging: true, placement: "Wearing"
}), Model.defaultBattery())

check("Off label", Model.noiseModeName(Model.NOISE_OFF), "Off")
check("ANC label", Model.noiseModeName(Model.NOISE_ANC), "Noise cancellation")
check("Ambient label", Model.noiseModeName(Model.NOISE_AMBIENT), "Ambient sound")
check("Adaptive label", Model.noiseModeName(Model.NOISE_ADAPTIVE), "Adaptive")
check("unknown mode label", Model.noiseModeName(Model.NOISE_UNKNOWN), "Unknown")
check("Buds4 Pro model label", Model.modelName("Buds4Pro"), "Galaxy Buds4 Pro")
check("unknown model remains useful", Model.modelName("FutureBuds"), "FutureBuds")
check("equalizer preset label", Model.equalizerPresetName(2), "Dynamic")
check("unknown equalizer preset has no label", Model.equalizerPresetName(-1), "")

check("unknown battery text", Model.levelText(Model.LEVEL_UNKNOWN), "--")
check("known battery text", Model.levelText(91), "91%")
check("unknown battery meter is empty", Model.levelFraction(Model.LEVEL_UNKNOWN), 0)
check("battery meter clamps high values", Model.levelFraction(130), 1)
check("battery meter clamps low values", Model.levelFraction(-5), 0)
check("charging takes precedence in battery metadata", Model.batteryMeta({
  available: true, charging: true, placement: "Wearing"
}), "Charging")
check("wearing metadata", Model.batteryMeta({
  available: true, charging: false, placement: "Wearing"
}), "In ear")
check("case metadata", Model.batteryMeta({
  available: true, charging: false, placement: "ClosedCase"
}), "In case")
check("unavailable metadata", Model.batteryMeta(Model.defaultBattery()), "Unavailable")
check("live snapshot has battery", Model.hasAnyBattery(live), true)

const longError = "first line\n" + "x".repeat(300)
const elided = Model.elideError(longError)
check("elided error is one line", elided.includes("\n"), false)
check("elided error fits the panel", elided.length <= Model.MAX_ERROR_CHARS, true)
check("empty error stays empty", Model.elideError(null), "")

if (failures > 0) {
  console.error(`${failures} model checks failed`)
  Deno.exit(1)
}

console.log("model.test.js: all checks passed")
