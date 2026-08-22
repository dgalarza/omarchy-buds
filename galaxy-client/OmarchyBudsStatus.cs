using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Constants;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;
using GalaxyBudsClient.Scripting;
using GalaxyBudsClient.Scripting.Hooks;

// Loaded by GalaxyBudsClient's CSScript hook manager. The hook publishes only
// state already decoded by GalaxyBudsClient; it never opens a Bluetooth socket
// or sends an earbud command.
public class OmarchyBudsStatus : IHook
{
    private const int SchemaVersion = 1;
    private readonly object _writeLock = new object();
    private readonly BluetoothImpl _bluetooth = BluetoothImpl.Instance;
    private readonly SppMessageReceiver _receiver = SppMessageReceiver.Instance;
    private readonly string _statusPath;
    private readonly string _temporaryPath;

    private IBasicStatusUpdate? _basicStatus;
    private ExtendedStatusUpdateDecoder? _extendedStatus;
    private bool _leftCharging;
    private bool _rightCharging;
    private bool _caseCharging;

    private ScriptLogger Logger => new ScriptLogger(this);

    public OmarchyBudsStatus()
    {
        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrWhiteSpace(stateHome))
        {
            stateHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "state"
            );
        }

        var stateDirectory = Path.Combine(stateHome, "omarchy-buds");
        _statusPath = Path.Combine(stateDirectory, "status.json");
        _temporaryPath = Path.Combine(stateDirectory, ".status.json.tmp");
    }

    public void OnHooked()
    {
        _receiver.ExtendedStatusUpdate += OnExtendedStatusUpdate;
        _receiver.StatusUpdate += OnStatusUpdate;
        _bluetooth.Connected += OnConnected;
        _bluetooth.Disconnected += OnDisconnected;
        _bluetooth.BluetoothError += OnBluetoothError;

        lock (_writeLock)
        {
            if (_bluetooth.IsConnected)
            {
                _extendedStatus = DeviceMessageCache.Instance.ExtendedStatusUpdate;
                _basicStatus = DeviceMessageCache.Instance.BasicStatusUpdate;
                if (_extendedStatus != null)
                {
                    _leftCharging = _extendedStatus.IsLeftCharging;
                    _rightCharging = _extendedStatus.IsRightCharging;
                    _caseCharging = _extendedStatus.IsCaseCharging;
                }
            }

            WriteSnapshotLocked(_bluetooth.IsConnected);
        }

        Logger.Info($"Publishing status schema {SchemaVersion} at {_statusPath}");
    }

    public void OnUnhooked()
    {
        _receiver.ExtendedStatusUpdate -= OnExtendedStatusUpdate;
        _receiver.StatusUpdate -= OnStatusUpdate;
        _bluetooth.Connected -= OnConnected;
        _bluetooth.Disconnected -= OnDisconnected;
        _bluetooth.BluetoothError -= OnBluetoothError;

        lock (_writeLock)
        {
            DeleteIfPresent(_temporaryPath);
            DeleteIfPresent(_statusPath);
        }
    }

    private void OnConnected(object? sender, EventArgs args)
    {
        lock (_writeLock)
        {
            _basicStatus = null;
            _extendedStatus = null;
            _leftCharging = false;
            _rightCharging = false;
            _caseCharging = false;
            WriteSnapshotLocked(true);
        }
    }

    private void OnDisconnected(object? sender, string reason)
    {
        PublishDisconnected();
    }

    private void OnBluetoothError(object? sender, BluetoothException error)
    {
        PublishDisconnected();
    }

    private void PublishDisconnected()
    {
        lock (_writeLock)
        {
            _basicStatus = null;
            _extendedStatus = null;
            _leftCharging = false;
            _rightCharging = false;
            _caseCharging = false;
            WriteSnapshotLocked(false);
        }
    }

    private void OnExtendedStatusUpdate(object? sender, ExtendedStatusUpdateDecoder status)
    {
        lock (_writeLock)
        {
            _extendedStatus = status;
            _basicStatus = status;
            _leftCharging = status.IsLeftCharging;
            _rightCharging = status.IsRightCharging;
            _caseCharging = status.IsCaseCharging;
            WriteSnapshotLocked(_bluetooth.IsConnected);
        }
    }

    private void OnStatusUpdate(object? sender, StatusUpdateDecoder status)
    {
        lock (_writeLock)
        {
            _basicStatus = status;
            _leftCharging = status.IsLeftCharging;
            _rightCharging = status.IsRightCharging;
            _caseCharging = status.IsCaseCharging;
            WriteSnapshotLocked(_bluetooth.IsConnected);
        }
    }

    private bool Supports(Features feature)
    {
        try
        {
            return _extendedStatus == null
                ? _bluetooth.DeviceSpec.Supports(feature)
                : _bluetooth.DeviceSpec.Supports(feature, _extendedStatus.Revision);
        }
        catch
        {
            return false;
        }
    }

    private bool HasTrayAction(TrayItemTypes action)
    {
        try
        {
            return _bluetooth.DeviceSpec.TrayShortcuts.Contains(action);
        }
        catch
        {
            return false;
        }
    }

    private Dictionary<string, object> Battery(
        int level,
        PlacementStates placement,
        bool charging,
        bool available)
    {
        return new Dictionary<string, object>
        {
            ["available"] = available,
            ["level"] = available ? level : -1,
            ["charging"] = available && charging,
            ["placement"] = available ? placement.ToString() : string.Empty
        };
    }

    private int NoiseMode(bool supportsNoiseControl, bool supportsAnc, bool supportsAmbient)
    {
        if (_extendedStatus == null)
            return -1;
        if (supportsNoiseControl)
            return (int)_extendedStatus.NoiseControlMode;
        if (supportsAnc && _extendedStatus.NoiseCancelling)
            return 1;
        if (supportsAmbient && _extendedStatus.AmbientSoundEnabled)
            return 2;
        return supportsAnc || supportsAmbient ? 0 : -1;
    }

    private void EqualizerState(out bool enabled, out int preset)
    {
        enabled = false;
        preset = -1;
        if (_extendedStatus == null)
            return;

        if (_bluetooth.CurrentModel == Models.Buds)
        {
            enabled = _extendedStatus.EqualizerEnabled;
            preset = _extendedStatus.EqualizerMode;
            if (preset > 4)
                preset -= 5;
        }
        else
        {
            enabled = _extendedStatus.EqualizerMode != 0;
            preset = enabled ? _extendedStatus.EqualizerMode - 1 : -1;
        }

        if (preset < 0 || preset > 4)
            preset = -1;
    }

    private Dictionary<string, object> BuildSnapshot(bool connected)
    {
        var hasExtendedStatus = connected && _extendedStatus != null;
        var supportsCaseBattery = connected && Supports(Features.CaseBattery);
        var supportsNoiseControl = hasExtendedStatus && Supports(Features.NoiseControl);
        var supportsAnc = hasExtendedStatus && Supports(Features.Anc);
        var supportsAmbient = hasExtendedStatus && Supports(Features.AmbientSound);
        var supportsConversation = hasExtendedStatus && Supports(Features.DetectConversations);
        var supportsOneEarbud = hasExtendedStatus && Supports(Features.NoiseControlsWithOneEarbud);
        var supportsEqualizer = hasExtendedStatus && HasTrayAction(TrayItemTypes.ToggleEqualizer);
        var supportsTouchLock = hasExtendedStatus && HasTrayAction(TrayItemTypes.LockTouchpad);

        var leftLevel = _basicStatus?.BatteryL ?? -1;
        var rightLevel = _basicStatus?.BatteryR ?? -1;
        var caseLevel = _basicStatus?.BatteryCase ?? -1;
        var leftPlacement = _basicStatus?.PlacementL ?? PlacementStates.Disconnected;
        var rightPlacement = _basicStatus?.PlacementR ?? PlacementStates.Disconnected;
        var leftAvailable = connected && leftLevel > 0 && leftLevel <= 100
            && leftPlacement != PlacementStates.Disconnected;
        var rightAvailable = connected && rightLevel > 0 && rightLevel <= 100
            && rightPlacement != PlacementStates.Disconnected;
        var caseAvailable = supportsCaseBattery && caseLevel > 0 && caseLevel <= 100;

        EqualizerState(out var equalizerEnabled, out var equalizerPreset);

        var currentModel = _bluetooth.CurrentModel;
        var model = currentModel == Models.NULL ? string.Empty : currentModel.ToString();
        var currentDevice = _bluetooth.Device.Current;
        var deviceName = connected ? _bluetooth.DeviceName : currentDevice?.Name ?? string.Empty;
        var address = currentDevice?.MacAddress ?? string.Empty;

        return new Dictionary<string, object>
        {
            ["schema_version"] = SchemaVersion,
            ["written_at"] = DateTime.UtcNow.ToString("O"),
            ["process_id"] = Environment.ProcessId,
            ["connected"] = connected,
            ["device_name"] = deviceName,
            ["model"] = model,
            ["address"] = address,
            ["battery"] = new Dictionary<string, object>
            {
                ["left"] = Battery(leftLevel, leftPlacement, _leftCharging, leftAvailable),
                ["right"] = Battery(rightLevel, rightPlacement, _rightCharging, rightAvailable),
                ["case"] = Battery(caseLevel, PlacementStates.Case, _caseCharging, caseAvailable)
            },
            ["capabilities"] = new Dictionary<string, object>
            {
                ["case_battery"] = supportsCaseBattery
            },
            ["noise_control"] = new Dictionary<string, object>
            {
                ["mode"] = NoiseMode(supportsNoiseControl, supportsAnc, supportsAmbient)
            },
            ["equalizer"] = new Dictionary<string, object>
            {
                ["enabled"] = hasExtendedStatus && equalizerEnabled,
                ["preset"] = hasExtendedStatus ? equalizerPreset : -1
            },
            ["touch_lock"] = new Dictionary<string, object>
            {
                ["enabled"] = hasExtendedStatus && _extendedStatus != null
                    && _extendedStatus.TouchpadLock
            },
            ["conversation_detection"] = new Dictionary<string, object>
            {
                ["enabled"] = supportsConversation && _extendedStatus != null
                    && _extendedStatus.DetectConversations
            },
            ["one_earbud_noise_control"] = new Dictionary<string, object>
            {
                ["enabled"] = supportsOneEarbud && _extendedStatus != null
                    && _extendedStatus.NoiseControlsWithOneEarbud
            },
            ["actions"] = new Dictionary<string, object>
            {
                ["anc_toggle"] = supportsAnc ? "AncToggle" : string.Empty,
                ["ambient_toggle"] = supportsAmbient ? "AmbientToggle" : string.Empty,
                ["equalizer_toggle"] = supportsEqualizer ? "EqualizerToggle" : string.Empty,
                ["touch_lock_toggle"] = supportsTouchLock ? "LockTouchpadToggle" : string.Empty,
                ["conversation_detection_toggle"] = supportsConversation
                    ? "ToggleConversationDetect"
                    : string.Empty,
                ["one_earbud_noise_control_toggle"] = supportsOneEarbud
                    ? "SwitchAncOne"
                    : string.Empty
            }
        };
    }

    private void WriteSnapshotLocked(bool connected)
    {
        try
        {
            var directory = Path.GetDirectoryName(_statusPath);
            if (string.IsNullOrEmpty(directory))
                return;

            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(BuildSnapshot(connected));

            using (var stream = new FileStream(
                _temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            File.Move(_temporaryPath, _statusPath, true);
        }
        catch (Exception error)
        {
            DeleteIfPresent(_temporaryPath);
            Logger.Error($"Could not publish status: {error.Message}");
        }
    }

    private void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception error)
        {
            Logger.Warning($"Could not remove {path}: {error.Message}");
        }
    }
}
