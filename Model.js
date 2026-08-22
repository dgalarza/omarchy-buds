// This file intentionally has no QML imports so its parsing functions can be
// exercised by Deno without loading Quickshell.

var SUPPORTED_SCHEMA = 1
var LEVEL_UNKNOWN = -1

var NOISE_UNKNOWN = -1
var NOISE_OFF = 0
var NOISE_ANC = 1
var NOISE_AMBIENT = 2
var NOISE_ADAPTIVE = 3

var ACTION_ANC_TOGGLE = "AncToggle"
var ACTION_AMBIENT_TOGGLE = "AmbientToggle"
var ACTION_EQUALIZER_TOGGLE = "EqualizerToggle"
var ACTION_TOUCH_LOCK_TOGGLE = "LockTouchpadToggle"
var ACTION_CONVERSATION_TOGGLE = "ToggleConversationDetect"
var ACTION_ONE_EARBUD_TOGGLE = "SwitchAncOne"

var MAX_ERROR_CHARS = 160
var ELIDED_ERROR_CHARS = 157

function defaultBattery() {
  return {
    available: false,
    level: LEVEL_UNKNOWN,
    charging: false,
    placement: ""
  }
}

function defaultActions() {
  return {
    ancToggle: "",
    ambientToggle: "",
    equalizerToggle: "",
    touchLockToggle: "",
    conversationToggle: "",
    oneEarbudToggle: ""
  }
}

function defaultStatus() {
  return {
    ok: false,
    lastError: "",
    schemaVersion: 0,
    schemaTooNew: false,
    writtenAt: "",
    processId: -1,
    connected: false,
    deviceName: "",
    model: "",
    address: "",
    left: defaultBattery(),
    right: defaultBattery(),
    caseBattery: defaultBattery(),
    supportsCaseBattery: false,
    noiseMode: NOISE_UNKNOWN,
    equalizerEnabled: false,
    equalizerPreset: -1,
    touchLocked: false,
    conversationDetection: false,
    oneEarbudNoiseControl: false,
    actions: defaultActions()
  }
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value)
}

function stringOr(value, fallback) {
  return typeof value === "string" ? value : fallback
}

function integerOr(value, fallback) {
  return typeof value === "number" && isFinite(value) && Math.floor(value) === value
    ? value
    : fallback
}

function batteryFrom(raw) {
  var battery = defaultBattery()
  if (!isObject(raw) || raw.available !== true) return battery

  var level = integerOr(raw.level, LEVEL_UNKNOWN)
  if (level < 0 || level > 100) return battery

  battery.available = true
  battery.level = level
  battery.charging = raw.charging === true
  battery.placement = stringOr(raw.placement, "")
  return battery
}

function actionFrom(raw, key, expected) {
  if (!isObject(raw)) return ""
  return raw[key] === expected ? expected : ""
}

function parseStatus(raw) {
  var status = defaultStatus()
  var text = String(raw === undefined || raw === null ? "" : raw).trim()
  if (text === "") {
    status.lastError = "The GalaxyBudsClient status file is empty"
    return status
  }

  var parsed
  try {
    parsed = JSON.parse(text)
  } catch (error) {
    status.lastError = "Could not parse the GalaxyBudsClient status file"
    return status
  }

  if (!isObject(parsed)) {
    status.lastError = "The GalaxyBudsClient status file is not a JSON object"
    return status
  }

  if (parsed.schema_version === undefined) {
    status.lastError = "The GalaxyBudsClient status file has no schema_version"
    return status
  }

  status.schemaVersion = integerOr(parsed.schema_version, 0)
  if (status.schemaVersion !== SUPPORTED_SCHEMA) {
    status.schemaTooNew = status.schemaVersion > SUPPORTED_SCHEMA
    status.lastError = status.schemaTooNew
      ? "The status hook writes schema " + status.schemaVersion + "; this panel reads " + SUPPORTED_SCHEMA
      : "Unsupported GalaxyBudsClient status schema " + status.schemaVersion
    return status
  }

  status.ok = true
  status.writtenAt = stringOr(parsed.written_at, "")
  var processId = integerOr(parsed.process_id, -1)
  status.processId = processId > 0 ? processId : -1
  status.connected = parsed.connected === true
  status.deviceName = stringOr(parsed.device_name, "")
  status.model = stringOr(parsed.model, "")
  status.address = stringOr(parsed.address, "")

  var battery = isObject(parsed.battery) ? parsed.battery : {}
  status.left = batteryFrom(battery.left)
  status.right = batteryFrom(battery.right)
  status.caseBattery = batteryFrom(battery["case"])

  var capabilities = isObject(parsed.capabilities) ? parsed.capabilities : {}
  status.supportsCaseBattery = capabilities.case_battery === true

  var noise = isObject(parsed.noise_control) ? parsed.noise_control : {}
  var mode = integerOr(noise.mode, NOISE_UNKNOWN)
  status.noiseMode = mode >= NOISE_OFF && mode <= NOISE_ADAPTIVE ? mode : NOISE_UNKNOWN

  var equalizer = isObject(parsed.equalizer) ? parsed.equalizer : {}
  status.equalizerEnabled = equalizer.enabled === true
  var preset = integerOr(equalizer.preset, -1)
  status.equalizerPreset = preset >= 0 && preset <= 4 ? preset : -1

  var touchLock = isObject(parsed.touch_lock) ? parsed.touch_lock : {}
  status.touchLocked = touchLock.enabled === true

  var conversation = isObject(parsed.conversation_detection) ? parsed.conversation_detection : {}
  status.conversationDetection = conversation.enabled === true

  var oneEarbud = isObject(parsed.one_earbud_noise_control)
    ? parsed.one_earbud_noise_control
    : {}
  status.oneEarbudNoiseControl = oneEarbud.enabled === true

  var actions = parsed.actions
  status.actions.ancToggle = actionFrom(actions, "anc_toggle", ACTION_ANC_TOGGLE)
  status.actions.ambientToggle = actionFrom(actions, "ambient_toggle", ACTION_AMBIENT_TOGGLE)
  status.actions.equalizerToggle = actionFrom(actions, "equalizer_toggle", ACTION_EQUALIZER_TOGGLE)
  status.actions.touchLockToggle = actionFrom(actions, "touch_lock_toggle", ACTION_TOUCH_LOCK_TOGGLE)
  status.actions.conversationToggle = actionFrom(actions, "conversation_detection_toggle", ACTION_CONVERSATION_TOGGLE)
  status.actions.oneEarbudToggle = actionFrom(actions, "one_earbud_noise_control_toggle", ACTION_ONE_EARBUD_TOGGLE)

  return status
}

function busctlProcessId(raw) {
  var match = String(raw || "").match(/^PID=([1-9][0-9]*)$/m)
  if (!match) return -1
  var processId = parseInt(match[1], 10)
  return isFinite(processId) && processId > 0 ? processId : -1
}

function noiseModeName(mode) {
  if (mode === NOISE_OFF) return "Off"
  if (mode === NOISE_ANC) return "Noise cancellation"
  if (mode === NOISE_AMBIENT) return "Ambient sound"
  if (mode === NOISE_ADAPTIVE) return "Adaptive"
  return "Unknown"
}

function modelName(model) {
  var names = {
    Buds: "Galaxy Buds",
    BudsPlus: "Galaxy Buds+",
    BudsLive: "Galaxy Buds Live",
    BudsPro: "Galaxy Buds Pro",
    Buds2: "Galaxy Buds2",
    Buds2Pro: "Galaxy Buds2 Pro",
    BudsFe: "Galaxy Buds FE",
    BudsCore: "Galaxy Buds Core",
    Buds3: "Galaxy Buds3",
    Buds3Pro: "Galaxy Buds3 Pro",
    Buds3Fe: "Galaxy Buds3 FE",
    Buds4: "Galaxy Buds4",
    Buds4Pro: "Galaxy Buds4 Pro"
  }
  return names[String(model || "")] || String(model || "")
}

function equalizerPresetName(preset) {
  var names = ["Bass boost", "Soft", "Dynamic", "Clear", "Treble boost"]
  return preset >= 0 && preset < names.length ? names[preset] : ""
}

function levelText(level) {
  return level === LEVEL_UNKNOWN ? "--" : String(level) + "%"
}

function levelFraction(level) {
  if (level === LEVEL_UNKNOWN) return 0
  return Math.max(0, Math.min(100, level)) / 100
}

function batteryMeta(battery) {
  if (!battery || battery.available !== true) return "Unavailable"
  if (battery.charging) return "Charging"
  if (battery.placement === "Wearing") return "In ear"
  if (battery.placement === "Case" || battery.placement === "ClosedCase") return "In case"
  if (battery.placement === "Idle") return "Out"
  return ""
}

function hasAnyBattery(status) {
  return !!status && (status.left.available || status.right.available || status.caseBattery.available)
}

function elideError(text) {
  var value = String(text || "").replace(/\s+/g, " ").trim()
  return value.length > MAX_ERROR_CHARS
    ? value.substring(0, ELIDED_ERROR_CHARS) + "…"
    : value
}
