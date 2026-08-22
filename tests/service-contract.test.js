// Run with: deno run --allow-read tests/service-contract.test.js
// This protects the liveness monitor wiring without starting Quickshell or a
// second bus monitor during the test.

const service = Deno.readTextFileSync(new URL("../Service.qml", import.meta.url))

let failures = 0

function check(name, passed) {
  if (passed) return
  failures++
  console.error(`FAIL ${name}`)
}

check("monitor filters the GalaxyBudsClient well-known name",
  service.includes("member='NameOwnerChanged',arg0='me.timschneeberger.GalaxyBudsClient'"))
check("monitor does not add a broad positional service match",
  !service.includes('"monitor", "org.freedesktop.DBus"'))
check("monitor reacts once per matching signal header",
  service.includes('indexOf("Member=NameOwnerChanged") >= 0'))
check("new snapshot PID mismatches trigger an immediate probe",
  service.includes("Model.snapshotNeedsProbe(parsed, clientProcessId)"))
check("the monitor is restarted after failure",
  service.includes("clientOwnerMonitorRestart.restart()"))
check("a low-frequency recovery probe remains enabled",
  /interval:\s*30000[\s\S]*triggeredOnStart:\s*true[\s\S]*onTriggered:\s*root\.probeClient\(\)/.test(service))

if (failures > 0) {
  console.error(`${failures} service contract checks failed`)
  Deno.exit(1)
}

console.log("service-contract.test.js: all checks passed")
