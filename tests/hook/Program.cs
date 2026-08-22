using System;
using System.IO;
using System.Text.Json;
using GalaxyBudsClient.Message;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Message.Parameter;
using GalaxyBudsClient.Model.Constants;
using GalaxyBudsClient.Model.Specifications;
using GalaxyBudsClient.Platform;

internal static class Program
{
    private static int _failures;
    private static string _statusPath = "";

    private static void Check(string name, bool passed)
    {
        if (passed)
            return;

        _failures++;
        Console.Error.WriteLine($"FAIL {name}");
    }

    private static JsonDocument Snapshot() => JsonDocument.Parse(File.ReadAllText(_statusPath));

    private static int IntValue(string section, string property)
    {
        using var snapshot = Snapshot();
        return snapshot.RootElement.GetProperty(section).GetProperty(property).GetInt32();
    }

    private static bool BoolValue(string section, string property)
    {
        using var snapshot = Snapshot();
        return snapshot.RootElement.GetProperty(section).GetProperty(property).GetBoolean();
    }

    private static AcknowledgementDecoder SimpleAcknowledgement(MsgIds id, byte value) => new()
    {
        Id = id,
        Parameters = new SimpleAckParameter { Value = value },
        RawParameters = new[] { value }
    };

    public static int Main()
    {
        var stateHome = Path.Combine(Path.GetTempPath(), $"omarchy-buds-hook-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", stateHome);
        _statusPath = Path.Combine(stateHome, "omarchy-buds", "status.json");

        var bluetooth = BluetoothImpl.Instance;
        bluetooth.IsConnected = true;
        bluetooth.CurrentModel = Models.Buds4Pro;
        bluetooth.DeviceName = "Test Buds";
        bluetooth.Device.Current = new DeviceStub
        {
            Name = "Test Buds",
            MacAddress = "00:11:22:33:44:55"
        };
        foreach (var feature in Enum.GetValues<Features>())
            bluetooth.DeviceSpec.SupportedFeatures.Add(feature);
        bluetooth.DeviceSpec.TrayShortcuts.Add(TrayItemTypes.ToggleEqualizer);
        bluetooth.DeviceSpec.TrayShortcuts.Add(TrayItemTypes.LockTouchpad);

        var extended = new ExtendedStatusUpdateDecoder
        {
            BatteryL = 80,
            BatteryR = 70,
            BatteryCase = 60,
            PlacementL = PlacementStates.Wearing,
            PlacementR = PlacementStates.Wearing,
            Revision = 1,
            NoiseControlMode = NoiseControlModes.Off,
            EqualizerMode = 3,
            TouchpadLock = false,
            DetectConversations = false,
            NoiseControlsWithOneEarbud = false
        };
        DeviceMessageCache.Instance.ExtendedStatusUpdate = extended;
        DeviceMessageCache.Instance.BasicStatusUpdate = extended;

        var receiver = SppMessageReceiver.Instance;
        var hook = new OmarchyBudsStatus();
        hook.OnHooked();

        Check("initial snapshot exists", File.Exists(_statusPath));
        Check("initial decoded noise mode is published", IntValue("noise_control", "mode") == 0);
        Check("initial decoded equalizer is published",
            BoolValue("equalizer", "enabled") && IntValue("equalizer", "preset") == 2);

        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.NOISE_CONTROLS, 1));
        Check("noise-control acknowledgement applies returned mode",
            IntValue("noise_control", "mode") == 1);
        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.NOISE_CONTROLS, 99));
        Check("invalid noise-control acknowledgement is ignored",
            IntValue("noise_control", "mode") == 1);
        receiver.RaiseNoiseControl(NoiseControlModes.Adaptive);
        Check("noise-control update applies returned mode",
            IntValue("noise_control", "mode") == 3);

        receiver.RaiseAmbient(true);
        Check("legacy ambient response enables ambient", IntValue("noise_control", "mode") == 2);
        receiver.RaiseAmbient(false);
        Check("legacy ambient response disables ambient", IntValue("noise_control", "mode") == 0);
        receiver.RaiseAnc(true);
        Check("legacy ANC response enables ANC", IntValue("noise_control", "mode") == 1);
        receiver.RaiseAnc(false);
        Check("legacy ANC response disables ANC", IntValue("noise_control", "mode") == 0);
        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.SET_NOISE_REDUCTION, 1));
        Check("legacy ANC acknowledgement applies returned state",
            IntValue("noise_control", "mode") == 1);

        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.EQUALIZER, 4));
        Check("equalizer acknowledgement applies returned preset",
            BoolValue("equalizer", "enabled") && IntValue("equalizer", "preset") == 3);
        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.EQUALIZER, 0));
        Check("equalizer acknowledgement applies returned disabled state",
            !BoolValue("equalizer", "enabled") && IntValue("equalizer", "preset") == 2);
        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.EQUALIZER, 99));
        Check("invalid equalizer acknowledgement is ignored",
            !BoolValue("equalizer", "enabled") && IntValue("equalizer", "preset") == 2);

        bluetooth.CurrentModel = Models.Buds;
        receiver.RaiseAcknowledgement(new AcknowledgementDecoder
        {
            Id = MsgIds.EQUALIZER,
            Parameters = new SimpleAckParameter { Value = 1 },
            RawParameters = new byte[] { 1, 9 }
        });
        Check("original Buds equalizer acknowledgement normalizes its preset",
            BoolValue("equalizer", "enabled") && IntValue("equalizer", "preset") == 4);
        bluetooth.CurrentModel = Models.Buds4Pro;

        receiver.RaiseAcknowledgement(new AcknowledgementDecoder
        {
            Id = MsgIds.LOCK_TOUCHPAD,
            Parameters = new LockTouchpadAckParameter { TouchpadLock = true },
            RawParameters = new byte[] { 1 }
        });
        Check("touch-lock acknowledgement applies returned state",
            BoolValue("touch_lock", "enabled"));

        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.SET_DETECT_CONVERSATIONS, 1));
        Check("conversation acknowledgement applies returned state",
            BoolValue("conversation_detection", "enabled"));

        receiver.RaiseAcknowledgement(SimpleAcknowledgement(MsgIds.SET_ANC_WITH_ONE_EARBUD, 1));
        Check("one-earbud acknowledgement applies returned state",
            BoolValue("one_earbud_noise_control", "enabled"));

        receiver.RaiseExtended(new ExtendedStatusUpdateDecoder
        {
            BatteryL = 80,
            BatteryR = 70,
            BatteryCase = 60,
            PlacementL = PlacementStates.Wearing,
            PlacementR = PlacementStates.Wearing,
            Revision = 1,
            NoiseControlMode = NoiseControlModes.AmbientSound,
            EqualizerMode = 0,
            TouchpadLock = false,
            DetectConversations = false,
            NoiseControlsWithOneEarbud = false
        });
        Check("later extended status overrides acknowledged noise state",
            IntValue("noise_control", "mode") == 2);
        Check("later extended status overrides acknowledged toggles",
            !BoolValue("touch_lock", "enabled")
            && !BoolValue("conversation_detection", "enabled")
            && !BoolValue("one_earbud_noise_control", "enabled"));

        var directoryMode = File.GetUnixFileMode(Path.GetDirectoryName(_statusPath)!);
        var fileMode = File.GetUnixFileMode(_statusPath);
        Check("state directory is private",
            directoryMode == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        Check("status file is private",
            fileMode == (UnixFileMode.UserRead | UnixFileMode.UserWrite));

        hook.OnUnhooked();
        Check("unhook removes the snapshot", !File.Exists(_statusPath));
        Directory.Delete(stateHome, true);

        if (_failures > 0)
        {
            Console.Error.WriteLine($"{_failures} hook behavior checks failed");
            return 1;
        }

        Console.WriteLine("hook behavior checks passed");
        return 0;
    }
}
