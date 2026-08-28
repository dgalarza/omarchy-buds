// Run with: deno run --allow-read tests/hook-contract.test.js
// GalaxyBudsClient owns the runtime types used by the hook, so this test keeps
// the source-level integration contract honest without loading the client or
// contacting earbuds.

const source = Deno.readTextFileSync(
  new URL("../galaxy-client/OmarchyBudsStatus.cs", import.meta.url)
)

let failures = 0

function check(name, passed) {
  if (passed) return
  failures++
  console.error(`FAIL ${name}`)
}

function contains(name, text) {
  check(name, source.includes(text))
}

for (const event of [
  "AcknowledgementResponse",
  "AmbientEnabledUpdateResponse",
  "AncEnabledUpdateResponse",
  "NoiseControlUpdateResponse"
]) {
  contains(`${event} is subscribed`, `_receiver.${event} +=`)
  contains(`${event} is unsubscribed`, `_receiver.${event} -=`)
}

for (const message of [
  "NOISE_CONTROLS",
  "SET_NOISE_REDUCTION",
  "EQUALIZER",
  "LOCK_TOUCHPAD",
  "SET_DETECT_CONVERSATIONS",
  "SET_ANC_WITH_ONE_EARBUD"
]) {
  contains(`${message} acknowledgement is handled`, `case MsgIds.${message}:`)
}

contains("noise acknowledgements apply the returned mode", "ApplyNoiseControlModeLocked(noiseMode.Value)")
contains("legacy ANC responses apply the returned value", "ApplyAncEnabledLocked(ancEnabled.Value != 0)")
contains("equalizer acknowledgements use returned bytes", "ApplyEqualizerConfirmationLocked(acknowledgement.RawParameters)")
contains("touch-lock acknowledgements normalize returned state",
  "_touchLocked = TouchLockFromAcknowledgement(touchLock)")
contains("advanced touch-lock acknowledgements invert touch-enabled state",
  "return Supports(Features.AdvancedTouchLock)\n            ? !touchLock.TouchpadLock\n            : touchLock.TouchpadLock")
contains("conversation acknowledgements use returned state", "_conversationDetection = conversationDetection.Value != 0")
contains("one-earbud acknowledgements use returned state", "_oneEarbudNoiseControl = oneEarbud.Value != 0")

check("local action dispatch is not treated as device state",
  !source.includes("EventDispatcher.Instance.EventReceived"))
check("local state is never toggled speculatively",
  !source.includes("_touchLocked = !_touchLocked"))

contains("state directory permissions are restricted",
  "File.SetUnixFileMode(directory, StateDirectoryMode)")
contains("state file permissions are restricted",
  "File.SetUnixFileMode(_temporaryPath, StateFileMode)")
contains("producer bounds device names", "MaxDeviceNameChars = 128")
contains("producer bounds the encoded snapshot", "MaxSnapshotBytes = 4 * 1024")
contains("producer checks the encoded snapshot size", "json.Length > MaxSnapshotBytes")
contains("temporary files cannot replace an existing path", "FileMode.CreateNew")

const serializeAt = source.indexOf("JsonSerializer.SerializeToUtf8Bytes")
const sizeCheckAt = source.indexOf("json.Length > MaxSnapshotBytes", serializeAt)
const tempWriteAt = source.indexOf("new FileStream(", serializeAt)
const durableFlushAt = source.indexOf("stream.Flush(true)", tempWriteAt)
const atomicMoveAt = source.indexOf("File.Move(_temporaryPath, _statusPath, true)", durableFlushAt)
check("snapshot serialization precedes its size check", serializeAt >= 0 && sizeCheckAt > serializeAt)
check("snapshot size is checked before the temporary write", tempWriteAt > sizeCheckAt)
check("temporary snapshot is flushed before replacement", durableFlushAt > tempWriteAt)
check("status replacement remains atomic", atomicMoveAt > durableFlushAt)

if (failures > 0) {
  console.error(`${failures} hook contract checks failed`)
  Deno.exit(1)
}

console.log("hook-contract.test.js: all checks passed")
