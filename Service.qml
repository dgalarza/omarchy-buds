import QtQuick
import Quickshell
import Quickshell.Io
import "Model.js" as Model

// GalaxyBudsClient owns the Bluetooth protocol connection. This service only
// consumes bounded helper output and invokes the client's documented CLI/D-Bus
// action surface.
Item {
  id: root

  property var settings: ({})
  property var status: Model.defaultStatus()
  property bool statusFilePresent: false
  property bool clientRunning: false
  property int clientProcessId: -1
  property string actionStatus: ""

  readonly property string statePath: (Quickshell.env("XDG_STATE_HOME")
    || Quickshell.env("HOME") + "/.local/state") + "/omarchy-buds/status.json"
  readonly property string helperPath: {
    var url = String(Qt.resolvedUrl("bin/omarchy-buds-helper"))
    return url.indexOf("file://") === 0
      ? decodeURIComponent(url.substring(7))
      : url
  }
  readonly property bool hookReady: statusFilePresent && status.ok
  readonly property bool snapshotCurrent: hookReady
    && Model.snapshotIsCurrent(status, clientProcessId)
  readonly property bool connected: clientRunning && snapshotCurrent && status.connected
  readonly property bool hasBattery: connected && Model.hasAnyBattery(status)
  readonly property bool busy: actionProcess.running
  readonly property string parseError: statusFilePresent && !status.ok ? status.lastError : ""

  readonly property string deviceName: status.deviceName
  readonly property string model: status.model
  readonly property var leftBattery: status.left
  readonly property var rightBattery: status.right
  readonly property var caseBattery: status.caseBattery
  readonly property bool supportsCaseBattery: status.supportsCaseBattery
  readonly property int noiseMode: status.noiseMode
  readonly property bool equalizerEnabled: status.equalizerEnabled
  readonly property int equalizerPreset: status.equalizerPreset
  readonly property bool touchLocked: status.touchLocked
  readonly property bool conversationDetection: status.conversationDetection
  readonly property bool oneEarbudNoiseControl: status.oneEarbudNoiseControl

  readonly property bool supportsAnc: connected && status.actions.ancToggle !== ""
  readonly property bool supportsAmbient: connected && status.actions.ambientToggle !== ""
  readonly property bool supportsEqualizer: connected && status.actions.equalizerToggle !== ""
  readonly property bool supportsTouchLock: connected && status.actions.touchLockToggle !== ""
  readonly property bool supportsConversationDetection: connected && status.actions.conversationToggle !== ""
  readonly property bool supportsOneEarbudNoiseControl: connected && status.actions.oneEarbudToggle !== ""

  function applyBridgeText(raw) {
    var result = Model.parseBridgeResult(raw)
    statusFilePresent = result.present
    status = result.status
    if (Model.snapshotNeedsProbe(status, clientProcessId)) probeClient()
  }

  function probeClient() {
    if (clientProbe.running) {
      deferredClientProbe.restart()
      return
    }
    clientProbe.running = true
  }

  function refresh() {
    restartStatusWatch()
    probeClient()
  }

  function restartStatusWatch() {
    if (statusWatch.running) statusWatch.running = false
    statusWatchRestart.restart()
  }

  function actionAllowed(action) {
    return action === Model.ACTION_ANC_TOGGLE
      || action === Model.ACTION_AMBIENT_TOGGLE
      || action === Model.ACTION_EQUALIZER_TOGGLE
      || action === Model.ACTION_TOUCH_LOCK_TOGGLE
      || action === Model.ACTION_CONVERSATION_TOGGLE
      || action === Model.ACTION_ONE_EARBUD_TOGGLE
  }

  function executeAction(action) {
    if (!connected || busy || !actionAllowed(action)) return
    actionStatus = ""
    actionProcess.command = [helperPath, "action", action]
    actionProcess.running = true
  }

  function toggleAnc() {
    executeAction(status.actions.ancToggle)
  }

  function toggleAmbient() {
    executeAction(status.actions.ambientToggle)
  }

  function toggleEqualizer() {
    executeAction(status.actions.equalizerToggle)
  }

  function toggleTouchLock() {
    executeAction(status.actions.touchLockToggle)
  }

  function toggleConversationDetection() {
    executeAction(status.actions.conversationToggle)
  }

  function toggleOneEarbudNoiseControl() {
    executeAction(status.actions.oneEarbudToggle)
  }

  function openClient() {
    if (!clientRunning || busy) return
    actionStatus = ""
    actionProcess.command = [helperPath, "action", "open-client"]
    actionProcess.running = true
  }

  // The helper opens the status path O_NOFOLLOW | O_NONBLOCK, verifies the
  // descriptor is a regular file, and emits only bounded canonical JSON.
  Process {
    id: statusWatch
    running: true
    command: [root.helperPath, "watch-status", root.statePath]
    stdout: SplitParser {
      onRead: function(line) { root.applyBridgeText(line) }
    }
    onExited: statusWatchRestart.restart()
  }

  Timer {
    id: statusWatchRestart
    interval: 1000
    repeat: false
    onTriggered: if (!statusWatch.running) statusWatch.running = true
  }

  // A crashed process cannot remove its hook output. Treat the status file as
  // live only while GalaxyBudsClient still owns its well-known session-bus
  // name, and react to owner changes instead of waiting for the fallback poll.
  Process {
    id: clientProbe
    running: false
    command: [root.helperPath, "owner-pid"]
    stdout: StdioCollector { id: clientProbeOut; waitForEnd: true }
    onExited: function(exitCode) {
      root.clientProcessId = exitCode === 0
        ? Model.helperProcessId(clientProbeOut.text)
        : -1
      root.clientRunning = exitCode === 0 && root.clientProcessId > 0
    }
  }

  Process {
    id: clientOwnerMonitor
    running: true
    command: [root.helperPath, "monitor-owner"]
    stdout: SplitParser {
      onRead: function(line) {
        if (String(line).trim() === "changed") root.probeClient()
      }
    }
    onExited: {
      root.probeClient()
      clientOwnerMonitorRestart.restart()
    }
  }

  Timer {
    id: clientOwnerMonitorRestart
    interval: 5000
    repeat: false
    onTriggered: {
      if (!clientOwnerMonitor.running) clientOwnerMonitor.running = true
      root.probeClient()
    }
  }

  Timer {
    id: deferredClientProbe
    interval: 100
    repeat: false
    onTriggered: root.probeClient()
  }

  Timer {
    interval: 30000
    repeat: true
    running: true
    triggeredOnStart: true
    onTriggered: root.probeClient()
  }

  Process {
    id: actionProcess
    running: false
    command: []
    stdout: StdioCollector { id: actionOut; waitForEnd: true }
    onExited: function(exitCode) {
      if (exitCode === 0) {
        root.actionStatus = "Action sent to GalaxyBudsClient"
        postActionRefresh.restart()
      } else {
        root.actionStatus = Model.elideError(actionOut.text
          || "GalaxyBudsClient rejected the action")
      }
      actionStatusTimer.restart()
      root.probeClient()
    }
  }

  Timer {
    id: postActionRefresh
    interval: 300
    repeat: false
    onTriggered: root.restartStatusWatch()
  }

  Timer {
    id: actionStatusTimer
    interval: 3000
    repeat: false
    onTriggered: root.actionStatus = ""
  }
}
