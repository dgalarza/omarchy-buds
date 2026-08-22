import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import Quickshell
import qs.Commons
import qs.Ui
import "Model.js" as Model

Panel {
  id: root

  moduleName: "io.github.dgalarza.omarchy-buds"
  ipcTarget: "io.github.dgalarza.omarchy-buds"

  property int cursorIndex: 0
  property bool cursorActive: false

  readonly property bool hideWhenDisconnected: setting("hideWhenDisconnected", true) === true
  readonly property color foreground: bar ? bar.foreground : Color.foreground
  readonly property color urgent: bar ? bar.urgent : Color.urgent
  readonly property color dim: Qt.darker(foreground, 1.45)
  readonly property string fontFamily: bar ? bar.fontFamily : Style.font.family
  readonly property color iconColor: buds.connected ? barForeground : Qt.darker(barForeground, 1.6)
  readonly property int lowBatteryPercent: 20

  readonly property string modelDisplay: Model.modelName(buds.model)
  readonly property bool hasNoiseSection: buds.connected
    && (buds.noiseMode !== Model.NOISE_UNKNOWN || buds.supportsAnc || buds.supportsAmbient)
  readonly property bool hasSettingsSection: buds.supportsEqualizer
    || buds.supportsTouchLock
    || buds.supportsConversationDetection
    || buds.supportsOneEarbudNoiseControl
  readonly property bool waitingForStatus: buds.connected && !buds.hasBattery
    && !root.hasNoiseSection && !root.hasSettingsSection

  readonly property string heroTitle: buds.connected
    ? (buds.deviceName !== "" ? buds.deviceName
      : (modelDisplay !== "" ? modelDisplay : "Galaxy Buds"))
    : "Galaxy Buds"
  readonly property string heroMeta: buds.connected
    ? modelDisplay
    : (!buds.clientRunning ? "GalaxyBudsClient is not running"
      : !buds.statusFilePresent ? "Status hook not loaded"
      : buds.parseError !== "" ? "Invalid status snapshot"
      : !buds.snapshotCurrent ? "Waiting for current status hook"
      : "Not connected")
  readonly property string heroDetail: buds.connected && buds.noiseMode !== Model.NOISE_UNKNOWN
    ? Model.noiseModeName(buds.noiseMode)
    : ""

  readonly property var cursorRows: {
    var rows = []
    if (buds.supportsAnc) rows.push("anc")
    if (buds.supportsAmbient) rows.push("ambient")
    if (buds.supportsEqualizer) rows.push("equalizer")
    if (buds.supportsTouchLock) rows.push("touch")
    if (buds.supportsConversationDetection) rows.push("conversation")
    if (buds.supportsOneEarbudNoiseControl) rows.push("one-earbud")
    if (buds.clientRunning) rows.push("open-client")
    return rows
  }
  readonly property string cursorRow: cursorRows.length === 0
    ? ""
    : cursorRows[Math.max(0, Math.min(cursorIndex, cursorRows.length - 1))]

  function rowHasCursor(name) {
    return cursorActive && cursorRow === name
  }

  function focusRow(name) {
    var index = cursorRows.indexOf(name)
    if (index < 0) return
    cursorActive = true
    cursorIndex = index
  }

  function moveCursor(delta) {
    if (cursorRows.length === 0) return
    cursorActive = true
    cursorIndex = Math.max(0, Math.min(cursorRows.length - 1, cursorIndex + delta))
  }

  function activateCursor() {
    if (cursorRow === "anc") buds.toggleAnc()
    else if (cursorRow === "ambient") buds.toggleAmbient()
    else if (cursorRow === "equalizer") buds.toggleEqualizer()
    else if (cursorRow === "touch") buds.toggleTouchLock()
    else if (cursorRow === "conversation") buds.toggleConversationDetection()
    else if (cursorRow === "one-earbud") buds.toggleOneEarbudNoiseControl()
    else if (cursorRow === "open-client") buds.openClient()
  }

  function guidanceText() {
    if (!buds.clientRunning)
      return "Start GalaxyBudsClient. It remains the only application connected to the earbuds."
    if (!buds.statusFilePresent)
      return "Run this plugin's setup script, then restart GalaxyBudsClient so it loads the status hook."
    if (buds.parseError !== "") return buds.parseError
    if (!buds.snapshotCurrent)
      return "The status file belongs to an earlier GalaxyBudsClient process. Restart the client after checking that the hook is installed."
    if (!buds.connected)
      return "Connect the earbuds in GalaxyBudsClient. Pairing and device management stay in the Bluetooth panel."
    if (waitingForStatus) return "Waiting for GalaxyBudsClient's first decoded status update."
    return ""
  }

  visible: !hideWhenDisconnected || buds.connected
  implicitWidth: button.implicitWidth
  implicitHeight: button.implicitHeight

  onOpenedChanged: if (opened) {
    cursorIndex = 0
    cursorActive = false
    if (panelFlick) panelFlick.contentY = 0
    buds.refresh()
  }

  Service {
    id: buds
    settings: root.settings
  }

  BarIconButton {
    id: button
    anchors.fill: parent
    bar: root.bar
    iconComponent: Component {
      GalaxyBudsIcon {
        anchors.centerIn: parent
        iconSize: Math.min(parent.width, parent.height) * 0.92
        color: root.iconColor
      }
    }
    onPressed: root.toggle()
  }

  KeyboardPanel {
    id: panel
    anchorItem: button
    owner: root
    bar: root.bar
    open: root.opened
    focusTarget: keyCatcher
    contentWidth: panel.fittedContentWidth(Style.space(380))
    contentHeight: panel.fittedContentHeight(panelColumn.implicitHeight, Style.space(560))

    PanelKeyCatcher {
      id: keyCatcher
      anchors.fill: parent
      onMoveRequested: function(dx, dy) {
        if (!root.cursorActive) {
          root.cursorActive = true
          return
        }
        if (dy !== 0) root.moveCursor(dy)
      }
      onActivateRequested: if (root.cursorActive) root.activateCursor()
      onCloseRequested: root.close()
      onTabRequested: function(direction) { root.switchPanel(direction) }
      onTextKey: function(text) {
        var key = String(text).toLowerCase()
        if (key === "r") buds.refresh()
        else if (key === "g") buds.openClient()
        else if (key === "n" && buds.supportsAnc) buds.toggleAnc()
        else if (key === "a" && buds.supportsAmbient) buds.toggleAmbient()
        else if (key === "e" && buds.supportsEqualizer) buds.toggleEqualizer()
        else if (key === "l" && buds.supportsTouchLock) buds.toggleTouchLock()
        else if (key === "c" && buds.supportsConversationDetection) buds.toggleConversationDetection()
        else if (key === "o" && buds.supportsOneEarbudNoiseControl) buds.toggleOneEarbudNoiseControl()
      }

      Flickable {
        id: panelFlick
        anchors.fill: parent
        contentWidth: width
        contentHeight: panelColumn.implicitHeight
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        flickableDirection: Flickable.VerticalFlick
        interactive: contentHeight > height
        ScrollBar.vertical: ScrollBar { policy: ScrollBar.AsNeeded }

        Column {
          id: panelColumn
          width: panelFlick.width
          spacing: Style.space(14)

          PanelHero {
            width: parent.width
            title: root.heroTitle
            meta: root.heroMeta
            detail: root.heroDetail
            foreground: root.foreground
            fontFamily: root.fontFamily
            iconOpacity: buds.connected ? 1.0 : 0.5
            iconComponent: Component {
              GalaxyBudsIcon {
                iconSize: Style.font.display
                color: buds.connected ? root.foreground : root.dim
              }
            }
          }

          Text {
            textFormat: Text.PlainText
            visible: buds.actionStatus !== ""
            width: parent.width
            text: buds.actionStatus
            color: root.dim
            font.family: root.fontFamily
            font.pixelSize: Style.font.bodySmall
            wrapMode: Text.WordWrap
          }

          Column {
            visible: buds.connected
            width: parent.width
            spacing: Style.space(8)

            PanelSeparator {
              foreground: root.foreground
            }

            PanelSectionHeader {
              text: "BATTERY"
              foreground: root.foreground
              fontFamily: root.fontFamily
            }

            BatteryRow {
              width: parent.width
              label: "Left"
              battery: buds.leftBattery
            }

            BatteryRow {
              width: parent.width
              label: "Right"
              battery: buds.rightBattery
            }

            BatteryRow {
              visible: buds.supportsCaseBattery
              height: visible ? implicitHeight : 0
              width: parent.width
              label: "Case"
              battery: buds.caseBattery
            }
          }

          Column {
            visible: root.hasNoiseSection
            width: parent.width
            spacing: Style.space(7)

            PanelSeparator {
              foreground: root.foreground
            }

            PanelSectionHeader {
              text: "NOISE CONTROL"
              foreground: root.foreground
              fontFamily: root.fontFamily
            }

            RowLayout {
              visible: buds.noiseMode !== Model.NOISE_UNKNOWN
              width: parent.width
              spacing: Style.space(8)

              Text {
                textFormat: Text.PlainText
                text: "Current mode"
                color: root.dim
                font.family: root.fontFamily
                font.pixelSize: Style.font.bodySmall
                Layout.fillWidth: true
              }

              Text {
                textFormat: Text.PlainText
                text: Model.noiseModeName(buds.noiseMode)
                color: root.foreground
                font.family: root.fontFamily
                font.pixelSize: Style.font.bodySmall
                font.bold: true
              }
            }

            ToggleRow {
              visible: buds.supportsAnc
              height: visible ? implicitHeight : 0
              width: parent.width
              rowName: "anc"
              label: "Noise cancellation"
              caption: "GalaxyBudsClient ANC action"
              checked: buds.noiseMode === Model.NOISE_ANC
              onTriggered: buds.toggleAnc()
            }

            ToggleRow {
              visible: buds.supportsAmbient
              height: visible ? implicitHeight : 0
              width: parent.width
              rowName: "ambient"
              label: "Ambient sound"
              caption: "GalaxyBudsClient ambient action"
              checked: buds.noiseMode === Model.NOISE_AMBIENT
              onTriggered: buds.toggleAmbient()
            }

            Text {
              textFormat: Text.PlainText
              visible: buds.noiseMode === Model.NOISE_ADAPTIVE
              width: parent.width
              text: "Adaptive is status-only here; GalaxyBudsClient exposes no explicit Adaptive action."
              color: root.dim
              font.family: root.fontFamily
              font.pixelSize: Style.font.caption
              wrapMode: Text.WordWrap
            }
          }

          Column {
            visible: root.hasSettingsSection
            width: parent.width
            spacing: Style.space(7)

            PanelSeparator {
              foreground: root.foreground
            }

            PanelSectionHeader {
              text: "EARBUD SETTINGS"
              foreground: root.foreground
              fontFamily: root.fontFamily
            }

            ToggleRow {
              visible: buds.supportsEqualizer
              height: visible ? implicitHeight : 0
              width: parent.width
              rowName: "equalizer"
              label: "Equalizer"
              caption: buds.equalizerEnabled && Model.equalizerPresetName(buds.equalizerPreset) !== ""
                ? Model.equalizerPresetName(buds.equalizerPreset)
                : "GalaxyBudsClient equalizer"
              checked: buds.equalizerEnabled
              onTriggered: buds.toggleEqualizer()
            }

            ToggleRow {
              visible: buds.supportsTouchLock
              height: visible ? implicitHeight : 0
              width: parent.width
              rowName: "touch"
              label: "Touch lock"
              caption: buds.touchLocked ? "Earbud touch input is locked" : "Earbud touch input is enabled"
              checked: buds.touchLocked
              onTriggered: buds.toggleTouchLock()
            }

            ToggleRow {
              visible: buds.supportsConversationDetection
              height: visible ? implicitHeight : 0
              width: parent.width
              rowName: "conversation"
              label: "Conversation detection"
              caption: "Switch to ambient sound when you speak"
              checked: buds.conversationDetection
              onTriggered: buds.toggleConversationDetection()
            }

            ToggleRow {
              visible: buds.supportsOneEarbudNoiseControl
              height: visible ? implicitHeight : 0
              width: parent.width
              rowName: "one-earbud"
              label: "One-earbud noise control"
              caption: "Allow noise control with one earbud in"
              checked: buds.oneEarbudNoiseControl
              onTriggered: buds.toggleOneEarbudNoiseControl()
            }
          }

          Text {
            textFormat: Text.PlainText
            visible: root.guidanceText() !== ""
            width: parent.width
            text: root.guidanceText()
            color: buds.parseError !== "" ? root.urgent : root.dim
            font.family: root.fontFamily
            font.pixelSize: Style.font.bodySmall
            horizontalAlignment: Text.AlignHCenter
            wrapMode: Text.WordWrap
          }

          Column {
            visible: buds.clientRunning
            width: parent.width
            spacing: Style.space(7)

            PanelSeparator {
              foreground: root.foreground
            }

            ActionRow {
              width: parent.width
              rowName: "open-client"
              label: "Open GalaxyBudsClient"
              caption: "Firmware, fit test, gestures, and detailed settings"
              onTriggered: buds.openClient()
            }
          }
        }
      }
    }
  }

  component BatteryRow: Item {
    id: batteryRow

    property string label: ""
    property var battery: Model.defaultBattery()

    readonly property bool low: battery.available
      && battery.level <= root.lowBatteryPercent
      && !battery.charging

    implicitHeight: batteryLayout.implicitHeight

    RowLayout {
      id: batteryLayout
      anchors.left: parent.left
      anchors.right: parent.right
      spacing: Style.space(8)

      Text {
        textFormat: Text.PlainText
        text: batteryRow.label
        color: root.foreground
        opacity: 0.65
        font.family: root.fontFamily
        font.pixelSize: Style.font.bodySmall
        Layout.preferredWidth: Style.space(42)
      }

      Rectangle {
        id: batteryTrack
        Layout.fillWidth: true
        Layout.alignment: Qt.AlignVCenter
        implicitHeight: Style.space(6)
        radius: height / 2
        color: Qt.darker(root.foreground, 3.1)

        Rectangle {
          width: batteryTrack.width * Model.levelFraction(batteryRow.battery.level)
          height: parent.height
          radius: parent.radius
          color: batteryRow.low ? root.urgent : root.foreground
        }
      }

      Text {
        textFormat: Text.PlainText
        text: Model.levelText(batteryRow.battery.level)
        color: root.foreground
        font.family: root.fontFamily
        font.pixelSize: Style.font.bodySmall
        horizontalAlignment: Text.AlignRight
        Layout.preferredWidth: Style.space(38)
      }

      Text {
        textFormat: Text.PlainText
        text: Model.batteryMeta(batteryRow.battery)
        color: root.dim
        font.family: root.fontFamily
        font.pixelSize: Style.font.caption
        horizontalAlignment: Text.AlignRight
        elide: Text.ElideRight
        Layout.preferredWidth: Style.space(72)
      }
    }
  }

  component ToggleRow: CursorSurface {
    id: toggleRow

    property string rowName: ""
    property string label: ""
    property string caption: ""
    property bool checked: false

    signal triggered()

    hasCursor: root.rowHasCursor(rowName)
    foreground: root.foreground
    opacity: buds.busy ? 0.65 : 1.0
    implicitHeight: toggleContent.implicitHeight + Style.spacing.rowPaddingX

    RowLayout {
      id: toggleContent
      anchors.left: parent.left
      anchors.right: parent.right
      anchors.verticalCenter: parent.verticalCenter
      anchors.leftMargin: Style.space(10)
      anchors.rightMargin: Style.space(6)
      spacing: Style.space(8)

      ColumnLayout {
        Layout.fillWidth: true
        spacing: Style.space(1)

        Text {
          textFormat: Text.PlainText
          Layout.fillWidth: true
          text: toggleRow.label
          color: root.foreground
          font.family: root.fontFamily
          font.pixelSize: Style.font.body
          elide: Text.ElideRight
        }

        Text {
          textFormat: Text.PlainText
          Layout.fillWidth: true
          text: toggleRow.caption
          color: root.dim
          font.family: root.fontFamily
          font.pixelSize: Style.font.caption
          elide: Text.ElideRight
        }
      }

      ToggleSwitch {
        Layout.alignment: Qt.AlignVCenter
        checked: toggleRow.checked
        busy: buds.busy
        interactive: false
        cursorRing: false
        foreground: root.foreground
      }
    }

    MouseArea {
      anchors.fill: parent
      enabled: !buds.busy
      hoverEnabled: true
      cursorShape: enabled ? Qt.PointingHandCursor : Qt.ArrowCursor
      onContainsMouseChanged: if (containsMouse) root.focusRow(toggleRow.rowName)
      onClicked: toggleRow.triggered()
    }
  }

  component ActionRow: CursorSurface {
    id: actionRow

    property string rowName: ""
    property string label: ""
    property string caption: ""

    signal triggered()

    hasCursor: root.rowHasCursor(rowName)
    foreground: root.foreground
    opacity: buds.busy ? 0.65 : 1.0
    implicitHeight: actionContent.implicitHeight + Style.spacing.rowPaddingX

    RowLayout {
      id: actionContent
      anchors.left: parent.left
      anchors.right: parent.right
      anchors.verticalCenter: parent.verticalCenter
      anchors.leftMargin: Style.space(10)
      anchors.rightMargin: Style.space(10)
      spacing: Style.space(8)

      ColumnLayout {
        Layout.fillWidth: true
        spacing: Style.space(1)

        Text {
          textFormat: Text.PlainText
          Layout.fillWidth: true
          text: actionRow.label
          color: root.foreground
          font.family: root.fontFamily
          font.pixelSize: Style.font.body
          elide: Text.ElideRight
        }

        Text {
          textFormat: Text.PlainText
          Layout.fillWidth: true
          text: actionRow.caption
          color: root.dim
          font.family: root.fontFamily
          font.pixelSize: Style.font.caption
          elide: Text.ElideRight
        }
      }

      Text {
        textFormat: Text.PlainText
        text: "→"
        color: root.foreground
        font.family: root.fontFamily
        font.pixelSize: Style.font.heading
      }
    }

    MouseArea {
      anchors.fill: parent
      enabled: !buds.busy
      hoverEnabled: true
      cursorShape: enabled ? Qt.PointingHandCursor : Qt.ArrowCursor
      onContainsMouseChanged: if (containsMouse) root.focusRow(actionRow.rowName)
      onClicked: actionRow.triggered()
    }
  }
}
