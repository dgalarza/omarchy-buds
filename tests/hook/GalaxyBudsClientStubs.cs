using System;
using System.Collections.Generic;
using GalaxyBudsClient.Message.Decoder;
using GalaxyBudsClient.Model.Constants;

namespace GalaxyBudsClient.Message
{
    public enum MsgIds
    {
        NOISE_CONTROLS,
        SET_NOISE_REDUCTION,
        EQUALIZER,
        LOCK_TOUCHPAD,
        SET_DETECT_CONVERSATIONS,
        SET_ANC_WITH_ONE_EARBUD
    }

    public sealed class DeviceMessageCache
    {
        public static DeviceMessageCache Instance { get; } = new DeviceMessageCache();
        public ExtendedStatusUpdateDecoder? ExtendedStatusUpdate { get; set; }
        public IBasicStatusUpdate? BasicStatusUpdate { get; set; }
    }

    public sealed class SppMessageReceiver
    {
        public static SppMessageReceiver Instance { get; } = new SppMessageReceiver();

        public event EventHandler<ExtendedStatusUpdateDecoder>? ExtendedStatusUpdate;
        public event EventHandler<StatusUpdateDecoder>? StatusUpdate;
        public event EventHandler<AcknowledgementDecoder>? AcknowledgementResponse;
        public event EventHandler<bool>? AmbientEnabledUpdateResponse;
        public event EventHandler<bool>? AncEnabledUpdateResponse;
        public event EventHandler<NoiseControlModes>? NoiseControlUpdateResponse;

        public void RaiseExtended(ExtendedStatusUpdateDecoder value) =>
            ExtendedStatusUpdate?.Invoke(this, value);
        public void RaiseStatus(StatusUpdateDecoder value) => StatusUpdate?.Invoke(this, value);
        public void RaiseAcknowledgement(AcknowledgementDecoder value) =>
            AcknowledgementResponse?.Invoke(this, value);
        public void RaiseAmbient(bool value) => AmbientEnabledUpdateResponse?.Invoke(this, value);
        public void RaiseAnc(bool value) => AncEnabledUpdateResponse?.Invoke(this, value);
        public void RaiseNoiseControl(NoiseControlModes value) =>
            NoiseControlUpdateResponse?.Invoke(this, value);
    }
}

namespace GalaxyBudsClient.Message.Decoder
{
    public interface IBasicStatusUpdate
    {
        int BatteryL { get; }
        int BatteryR { get; }
        int BatteryCase { get; }
        PlacementStates PlacementL { get; }
        PlacementStates PlacementR { get; }
    }

    public class ExtendedStatusUpdateDecoder : IBasicStatusUpdate
    {
        public int BatteryL { get; set; }
        public int BatteryR { get; set; }
        public int BatteryCase { get; set; }
        public PlacementStates PlacementL { get; set; }
        public PlacementStates PlacementR { get; set; }
        public bool IsLeftCharging { get; set; }
        public bool IsRightCharging { get; set; }
        public bool IsCaseCharging { get; set; }
        public int Revision { get; set; }
        public NoiseControlModes NoiseControlMode { get; set; }
        public bool NoiseCancelling { get; set; }
        public bool AmbientSoundEnabled { get; set; }
        public bool EqualizerEnabled { get; set; }
        public int EqualizerMode { get; set; }
        public bool TouchpadLock { get; set; }
        public bool DetectConversations { get; set; }
        public bool NoiseControlsWithOneEarbud { get; set; }
    }

    public class StatusUpdateDecoder : IBasicStatusUpdate
    {
        public int BatteryL { get; set; }
        public int BatteryR { get; set; }
        public int BatteryCase { get; set; }
        public PlacementStates PlacementL { get; set; }
        public PlacementStates PlacementR { get; set; }
        public bool IsLeftCharging { get; set; }
        public bool IsRightCharging { get; set; }
        public bool IsCaseCharging { get; set; }
    }

    public class AcknowledgementDecoder
    {
        public GalaxyBudsClient.Message.MsgIds Id { get; set; }
        public GalaxyBudsClient.Message.Parameter.IAckParameter? Parameters { get; set; }
        public byte[]? RawParameters { get; set; }
    }
}

namespace GalaxyBudsClient.Message.Parameter
{
    public interface IAckParameter { }

    public class SimpleAckParameter : IAckParameter
    {
        public byte Value { get; set; }
    }

    public class LockTouchpadAckParameter : IAckParameter
    {
        public bool TouchpadLock { get; set; }
    }
}

namespace GalaxyBudsClient.Model
{
}

namespace GalaxyBudsClient.Model.Constants
{
    public enum Models
    {
        NULL,
        Buds,
        Buds4Pro
    }

    public enum PlacementStates
    {
        Disconnected,
        Wearing,
        Idle,
        Case
    }

    public enum NoiseControlModes
    {
        Off = 0,
        NoiseReduction = 1,
        AmbientSound = 2,
        Adaptive = 3
    }
}

namespace GalaxyBudsClient.Model.Specifications
{
    public enum Features
    {
        NoiseControl,
        Anc,
        AmbientSound,
        DetectConversations,
        NoiseControlsWithOneEarbud,
        CaseBattery,
        AdvancedTouchLock
    }

    public enum TrayItemTypes
    {
        ToggleEqualizer,
        LockTouchpad
    }

    public sealed class DeviceSpecStub
    {
        public HashSet<Features> SupportedFeatures { get; } = new HashSet<Features>();
        public List<TrayItemTypes> TrayShortcuts { get; } = new List<TrayItemTypes>();
        public bool Supports(Features feature) => SupportedFeatures.Contains(feature);
        public bool Supports(Features feature, int revision) => SupportedFeatures.Contains(feature);
    }
}

namespace GalaxyBudsClient.Platform
{
    using GalaxyBudsClient.Model.Specifications;

    public sealed class BluetoothException : Exception
    {
        public BluetoothException(string message) : base(message) { }
    }

    public sealed class DeviceStub
    {
        public string Name { get; set; } = "";
        public string MacAddress { get; set; } = "";
    }

    public sealed class DeviceManagerStub
    {
        public DeviceStub? Current { get; set; }
    }

    public sealed class BluetoothImpl
    {
        public static BluetoothImpl Instance { get; } = new BluetoothImpl();

        public event EventHandler? Connected;
        public event EventHandler<string>? Disconnected;
        public event EventHandler<BluetoothException>? BluetoothError;

        public bool IsConnected { get; set; }
        public Models CurrentModel { get; set; }
        public string DeviceName { get; set; } = "";
        public DeviceSpecStub DeviceSpec { get; } = new DeviceSpecStub();
        public DeviceManagerStub Device { get; } = new DeviceManagerStub();

        public void RaiseConnected() => Connected?.Invoke(this, EventArgs.Empty);
        public void RaiseDisconnected(string reason) => Disconnected?.Invoke(this, reason);
        public void RaiseError(string reason) =>
            BluetoothError?.Invoke(this, new BluetoothException(reason));
    }
}

namespace GalaxyBudsClient.Scripting
{
    public sealed class ScriptLogger
    {
        public ScriptLogger(GalaxyBudsClient.Scripting.Hooks.IHook hook) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }
}

namespace GalaxyBudsClient.Scripting.Hooks
{
    public interface IHook
    {
        void OnHooked();
        void OnUnhooked();
    }
}
