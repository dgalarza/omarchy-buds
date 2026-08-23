// Run with: deno run --allow-read tests/plugin-contract.test.js
// Protect the marketplace bar-widget and nested-panel lifecycle shape.

const root = new URL("../", import.meta.url)
const manifest = JSON.parse(Deno.readTextFileSync(new URL("manifest.json", root)))
const barWidget = Deno.readTextFileSync(new URL("BarWidget.qml", root))
const panel = Deno.readTextFileSync(new URL("Panel.qml", root))

let failures = 0

function check(name, passed) {
  if (passed) return
  failures++
  console.error(`FAIL ${name}`)
}

check("manifest uses the permanent plugin ID",
  manifest.id === "io.github.dgalarza.omarchy-buds")
check("manifest declares one bar-widget kind",
  JSON.stringify(manifest.kinds) === JSON.stringify(["bar-widget"]))
check("manifest loads BarWidget.qml",
  manifest.entryPoints?.barWidget === "BarWidget.qml")
check("bar entry point uses BarWidget", /\bBarWidget\s*\{/.test(barWidget))
check("bar entry point loads the nested panel",
  barWidget.includes('source: Qt.resolvedUrl("Panel.qml")'))

for (const member of [
  "readonly property bool opened",
  "function open()",
  "function close()",
  "function toggle()",
  "readonly property bool popoutSwitchClosing",
  "function closeForPopoutSwitch()"
]) {
  check(`bar entry point forwards ${member}`, barWidget.includes(member))
}

check("bar entry point injects the panel anchor",
  barWidget.includes("target.anchorItem = button"))
check("bar entry point injects its host identity",
  barWidget.includes("target.hostWidget = root"))
check("nested panel disables duplicate IPC management",
  panel.includes("manageIpc: false"))
check("nested panel accepts the bar anchor",
  panel.includes("property var anchorItem: null"))
check("nested panel accepts its host widget",
  panel.includes("property var hostWidget: null"))
check("nested panel anchors KeyboardPanel to the bar button",
  panel.includes("anchorItem: root.anchorItem"))
check("cursor navigation scrolls the selected row into view",
  panel.includes("function ensureCursorVisible()")
  && panel.includes("item.mapToItem(panelColumn, 0, 0)")
  && panel.includes("Qt.callLater(root.ensureCursorVisible)"))
check("touch lock uses a key not reserved by PanelKeyCatcher",
  panel.includes('key === "t" && buds.supportsTouchLock')
  && !panel.includes('key === "l" && buds.supportsTouchLock'))
check("marketplace preview exists at repository root",
  Deno.statSync(new URL("preview.png", root)).isFile)

if (failures > 0) {
  console.error(`${failures} plugin contract checks failed`)
  Deno.exit(1)
}

console.log("plugin-contract.test.js: all checks passed")
