import QtQuick
import qs.Ui

// Bar entry point and lifecycle bridge for the Omarchy Buds details panel.
BarWidget {
  id: root

  moduleName: "io.github.dgalarza.omarchy-buds"

  readonly property bool opened: panelLoader.item
    ? panelLoader.item.opened === true
    : false
  readonly property bool popoutSwitchClosing: panelLoader.item
    ? panelLoader.item.popoutSwitchClosing === true
    : false
  readonly property bool connected: panelLoader.item
    ? panelLoader.item.connected === true
    : false
  readonly property bool hideWhenDisconnected: panelLoader.item
    ? panelLoader.item.hideWhenDisconnected === true
    : true

  function open() {
    if (panelLoader.item) panelLoader.item.open()
  }

  function close() {
    if (panelLoader.item) panelLoader.item.close()
  }

  function toggle() {
    if (panelLoader.item) panelLoader.item.toggle()
  }

  function closeForPopoutSwitch() {
    if (panelLoader.item) panelLoader.item.closeForPopoutSwitch()
  }

  function injectPanel() {
    var target = panelLoader.item
    if (!target) return
    target.bar = root.bar
    target.settings = root.settings
    target.anchorItem = button
    target.hostWidget = root
  }

  visible: panelLoader.item && (!hideWhenDisconnected || connected)
  implicitWidth: button.implicitWidth
  implicitHeight: button.implicitHeight

  onBarChanged: injectPanel()
  onSettingsChanged: injectPanel()

  Loader {
    id: panelLoader
    active: true
    source: Qt.resolvedUrl("Panel.qml")
    visible: false
    onLoaded: {
      root.injectPanel()
      Qt.callLater(root.injectPanel)
    }
  }

  BarIconButton {
    id: button
    anchors.fill: parent
    bar: root.bar
    tooltipText: "Open Omarchy Buds"
    iconComponent: Component {
      GalaxyBudsIcon {
        anchors.centerIn: parent
        iconSize: Math.min(parent.width, parent.height) * 0.92
        color: root.connected ? button.foreground : Qt.darker(button.foreground, 1.6)
      }
    }
    onPressed: function(buttonCode) {
      if (buttonCode === Qt.LeftButton) root.toggle()
    }
  }
}
