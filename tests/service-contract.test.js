// Run with: deno run --allow-read tests/service-contract.test.js
// This protects the liveness monitor wiring without starting Quickshell or a
// second bus monitor during the test.

const service = Deno.readTextFileSync(new URL("../Service.qml", import.meta.url))
const helper = Deno.readTextFileSync(new URL("../bin/omarchy-buds-helper", import.meta.url))

let failures = 0

function check(name, passed) {
  if (passed) return
  failures++
  console.error(`FAIL ${name}`)
}

check("QML never reads the status path through FileView", !service.includes("FileView"))
check("QML status comes only from the packaged helper",
  service.includes('[root.helperPath, "watch-status", root.statePath]'))
check("owner probe uses the bounded helper", service.includes('[root.helperPath, "owner-pid"]'))
check("owner monitor uses the bounded helper", service.includes('[root.helperPath, "monitor-owner"]'))
check("actions use the bounded helper", service.includes('[helperPath, "action", action]'))
check("raw action stderr has no QML collector", !service.includes("actionErr"))
check("helper opens status without following symlinks",
  helper.includes("O_RDONLY | O_NONBLOCK | O_NOFOLLOW"))
check("helper verifies the opened descriptor is regular",
  helper.includes('S_ISREG($metadata[2])'))
check("helper enforces status input and canonical output ceilings",
  helper.includes("MAX_STATUS_BYTES => 8 * 1024")
  && helper.includes("MAX_CANONICAL_BYTES => 4 * 1024"))
check("owner probe uses the narrow D-Bus PID method",
  helper.includes('"GetConnectionUnixProcessID"'))
check("owner probe does not use broad busctl status", !helper.includes('"status",'))
check("monitor filters the GalaxyBudsClient well-known name",
  helper.includes("member='NameOwnerChanged',arg0='me.timschneeberger.GalaxyBudsClient'"))
check("monitor emits only a fixed change token", helper.includes('print STDOUT "changed\\n"'))
check("new snapshot PID mismatches trigger an immediate probe",
  service.includes("Model.snapshotNeedsProbe(status, clientProcessId)"))
check("the monitor is restarted after failure",
  service.includes("clientOwnerMonitorRestart.restart()"))
check("monitor restart closes the owner-change gap",
  /id:\s*clientOwnerMonitorRestart[\s\S]*onTriggered:\s*\{[\s\S]*root\.probeClient\(\)/.test(service))
check("a low-frequency recovery probe remains enabled",
  /interval:\s*30000[\s\S]*triggeredOnStart:\s*true[\s\S]*onTriggered:\s*root\.probeClient\(\)/.test(service))

if (failures > 0) {
  console.error(`${failures} service contract checks failed`)
  Deno.exit(1)
}

console.log("service-contract.test.js: all checks passed")
