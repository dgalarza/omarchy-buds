using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Message.Parameter;
using GalaxyBudsClient.Model;
using GalaxyBudsClient.Model.Constants;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;
using GalaxyBudsClient.Scripting;
using GalaxyBudsClient.Scripting.Hooks;

// Loaded by GalaxyBudsClient's CSScript hook manager. The hook publishes
// decoded status and device-confirmed control responses; it never opens a
// Bluetooth socket or sends an earbud command.
public class OmarchyBudsStatus : IHook
{
    private const int SchemaVersion = 1;
    private const UnixFileMode StateDirectoryMode = UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute;
    private const UnixFileMode StateFileMode = UnixFileMode.UserRead
        | UnixFileMode.UserWrite;
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
    private int _noiseMode = -1;
    private bool _equalizerEnabled;
    private int _equalizerPreset = -1;
    private bool _touchLocked;
    private bool _conversationDetection;
    private bool _oneEarbudNoiseControl;

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
        _receiver.AcknowledgementResponse += OnAcknowledgement;
        _receiver.AmbientEnabledUpdateResponse += OnAmbientEnabledConfirmed;
        _receiver.AncEnabledUpdateResponse += OnAncEnabledConfirmed;
        _receiver.NoiseControlUpdateResponse += OnNoiseControlConfirmed;
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
                    ApplyDecodedControlStateLocked(_extendedStatus);
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
        _receiver.AcknowledgementResponse -= OnAcknowledgement;
        _receiver.AmbientEnabledUpdateResponse -= OnAmbientEnabledConfirmed;
        _receiver.AncEnabledUpdateResponse -= OnAncEnabledConfirmed;
        _receiver.NoiseControlUpdateResponse -= OnNoiseControlConfirmed;
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
            ResetStatusLocked();
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
            ResetStatusLocked();
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
            ApplyDecodedControlStateLocked(status);
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

    private void ResetStatusLocked()
    {
        _basicStatus = null;
        _extendedStatus = null;
        _leftCharging = false;
        _rightCharging = false;
        _caseCharging = false;
        _noiseMode = -1;
        _equalizerEnabled = false;
        _equalizerPreset = -1;
        _touchLocked = false;
        _conversationDetection = false;
        _oneEarbudNoiseControl = false;
    }

    private void ApplyDecodedControlStateLocked(ExtendedStatusUpdateDecoder status)
    {
        var supportsNoiseControl = Supports(Features.NoiseControl);
        var supportsAnc = Supports(Features.Anc);
        var supportsAmbient = Supports(Features.AmbientSound);

        if (supportsNoiseControl)
            _noiseMode = (int)status.NoiseControlMode;
        else if (supportsAnc && status.NoiseCancelling)
            _noiseMode = 1;
        else if (supportsAmbient && status.AmbientSoundEnabled)
            _noiseMode = 2;
        else
            _noiseMode = supportsAnc || supportsAmbient ? 0 : -1;

        ReadEqualizerState(status, out _equalizerEnabled, out _equalizerPreset);
        _touchLocked = status.TouchpadLock;
        _conversationDetection = status.DetectConversations;
        _oneEarbudNoiseControl = status.NoiseControlsWithOneEarbud;
    }

    // Control values change only after a decoded device response. This avoids
    // presenting GalaxyBudsClient's locally dispatched toggle as earbud state.
    private void OnAcknowledgement(object? sender, AcknowledgementDecoder acknowledgement)
    {
        lock (_writeLock)
        {
            if (!_bluetooth.IsConnected || _extendedStatus == null)
                return;

            var confirmed = false;
            switch (acknowledgement.Id)
            {
                case MsgIds.NOISE_CONTROLS:
                    if (acknowledgement.Parameters is SimpleAckParameter noiseMode)
                        confirmed = ApplyNoiseControlModeLocked(noiseMode.Value);
                    break;
                case MsgIds.SET_NOISE_REDUCTION:
                    if (acknowledgement.Parameters is SimpleAckParameter ancEnabled)
                        confirmed = ApplyAncEnabledLocked(ancEnabled.Value != 0);
                    break;
                case MsgIds.EQUALIZER:
                    confirmed = ApplyEqualizerConfirmationLocked(acknowledgement.RawParameters);
                    break;
                case MsgIds.LOCK_TOUCHPAD:
                    if (acknowledgement.Parameters is LockTouchpadAckParameter touchLock)
                    {
                        _touchLocked = touchLock.TouchpadLock;
                        confirmed = true;
                    }
                    break;
                case MsgIds.SET_DETECT_CONVERSATIONS:
                    if (acknowledgement.Parameters is SimpleAckParameter conversationDetection)
                    {
                        _conversationDetection = conversationDetection.Value != 0;
                        confirmed = true;
                    }
                    break;
                case MsgIds.SET_ANC_WITH_ONE_EARBUD:
                    if (acknowledgement.Parameters is SimpleAckParameter oneEarbud)
                    {
                        _oneEarbudNoiseControl = oneEarbud.Value != 0;
                        confirmed = true;
                    }
                    break;
            }

            if (confirmed)
                WriteSnapshotLocked(true);
        }
    }

    private void OnAmbientEnabledConfirmed(object? sender, bool enabled)
    {
        lock (_writeLock)
        {
            if (_bluetooth.IsConnected && _extendedStatus != null
                && ApplyAmbientEnabledLocked(enabled))
            {
                WriteSnapshotLocked(true);
            }
        }
    }

    private void OnAncEnabledConfirmed(object? sender, bool enabled)
    {
        lock (_writeLock)
        {
            if (_bluetooth.IsConnected && _extendedStatus != null
                && ApplyAncEnabledLocked(enabled))
            {
                WriteSnapshotLocked(true);
            }
        }
    }

    private void OnNoiseControlConfirmed(object? sender, NoiseControlModes mode)
    {
        lock (_writeLock)
        {
            if (_bluetooth.IsConnected && _extendedStatus != null
                && ApplyNoiseControlModeLocked((int)mode))
            {
                WriteSnapshotLocked(true);
            }
        }
    }

    private bool ApplyNoiseControlModeLocked(int mode)
    {
        if (mode < (int)NoiseControlModes.Off || mode > (int)NoiseControlModes.Adaptive)
            return false;

        _noiseMode = mode;
        return true;
    }

    private bool ApplyAmbientEnabledLocked(bool enabled)
    {
        if (enabled)
            _noiseMode = (int)NoiseControlModes.AmbientSound;
        else if (_noiseMode == (int)NoiseControlModes.AmbientSound)
            _noiseMode = (int)NoiseControlModes.Off;

        return true;
    }

    private bool ApplyAncEnabledLocked(bool enabled)
    {
        if (enabled)
            _noiseMode = (int)NoiseControlModes.NoiseReduction;
        else if (_noiseMode == (int)NoiseControlModes.NoiseReduction)
            _noiseMode = (int)NoiseControlModes.Off;

        return true;
    }

    private bool ApplyEqualizerConfirmationLocked(byte[]? values)
    {
        if (values == null || values.Length == 0)
            return false;

        if (_bluetooth.CurrentModel == Models.Buds)
        {
            _equalizerEnabled = values[0] != 0;
            if (values.Length > 1)
            {
                var preset = values[1];
                if (preset > 4)
                    preset -= 5;
                _equalizerPreset = preset <= 4 ? preset : -1;
            }
            return true;
        }

        var mode = values[0];
        if (mode > 5)
            return false;

        _equalizerEnabled = mode != 0;
        _equalizerPreset = mode == 0 ? 2 : mode - 1;
        return true;
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

    private void ReadEqualizerState(
        ExtendedStatusUpdateDecoder status,
        out bool enabled,
        out int preset)
    {
        if (_bluetooth.CurrentModel == Models.Buds)
        {
            enabled = status.EqualizerEnabled;
            preset = status.EqualizerMode;
            if (preset > 4)
                preset -= 5;
        }
        else
        {
            enabled = status.EqualizerMode != 0;
            // GalaxyBudsClient keeps Dynamic selected while EQ is disabled.
            preset = enabled ? status.EqualizerMode - 1 : 2;
        }

        if (preset < 0 || preset > 4)
            preset = -1;
    }

    private Dictionary<string, object> BuildSnapshot(bool connected)
    {
        var hasExtendedStatus = connected && _extendedStatus != null;
        var supportsCaseBattery = connected && Supports(Features.CaseBattery);
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
                ["mode"] = hasExtendedStatus ? _noiseMode : -1
            },
            ["equalizer"] = new Dictionary<string, object>
            {
                ["enabled"] = hasExtendedStatus && _equalizerEnabled,
                ["preset"] = hasExtendedStatus ? _equalizerPreset : -1
            },
            ["touch_lock"] = new Dictionary<string, object>
            {
                ["enabled"] = hasExtendedStatus && _touchLocked
            },
            ["conversation_detection"] = new Dictionary<string, object>
            {
                ["enabled"] = supportsConversation && _conversationDetection
            },
            ["one_earbud_noise_control"] = new Dictionary<string, object>
            {
                ["enabled"] = supportsOneEarbud && _oneEarbudNoiseControl
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
            File.SetUnixFileMode(directory, StateDirectoryMode);
            var json = JsonSerializer.Serialize(BuildSnapshot(connected));

            using (var stream = new FileStream(
                _temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                File.SetUnixFileMode(_temporaryPath, StateFileMode);
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
