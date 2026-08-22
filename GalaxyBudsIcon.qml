import QtQuick
import qs.Commons

// A small geometric earbuds mark drawn for this plugin. It is intentionally
// generic rather than a traced Samsung product image.
Item {
  id: root

  property real iconSize: 16
  property color color: Color.foreground

  implicitWidth: iconSize
  implicitHeight: iconSize

  Item {
    width: root.iconSize
    height: root.iconSize

    // Left earbud.
    Rectangle {
      x: root.iconSize * 0.08
      y: root.iconSize * 0.14
      width: root.iconSize * 0.34
      height: root.iconSize * 0.38
      radius: width / 2
      color: root.color
      rotation: -12
      antialiasing: true
    }

    Rectangle {
      x: root.iconSize * 0.27
      y: root.iconSize * 0.42
      width: root.iconSize * 0.12
      height: root.iconSize * 0.43
      radius: width / 2
      color: root.color
      rotation: -7
      antialiasing: true
    }

    // Right earbud.
    Rectangle {
      x: root.iconSize * 0.58
      y: root.iconSize * 0.14
      width: root.iconSize * 0.34
      height: root.iconSize * 0.38
      radius: width / 2
      color: root.color
      rotation: 12
      antialiasing: true
    }

    Rectangle {
      x: root.iconSize * 0.61
      y: root.iconSize * 0.42
      width: root.iconSize * 0.12
      height: root.iconSize * 0.43
      radius: width / 2
      color: root.color
      rotation: 7
      antialiasing: true
    }
  }
}
