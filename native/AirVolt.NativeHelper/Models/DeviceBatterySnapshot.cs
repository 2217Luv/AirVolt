using System.Text.Json.Serialization;

namespace AirVolt.NativeHelper.Models;

public enum DeviceKind
{
    [JsonPropertyName("mouse")]
    Mouse,
    [JsonPropertyName("keyboard")]
    Keyboard,
    [JsonPropertyName("headset")]
    Headset,
    [JsonPropertyName("controller")]
    Controller,
    [JsonPropertyName("pen")]
    Pen,
    [JsonPropertyName("unknown")]
    Unknown
}

public enum DeviceConnection
{
    [JsonPropertyName("bluetooth-le")]
    BluetoothLE,
    [JsonPropertyName("bluetooth-classic")]
    BluetoothClassic,
    [JsonPropertyName("usb-2.4g")]
    Usb24G,
    [JsonPropertyName("usb")]
    Usb,
    [JsonPropertyName("unknown")]
    Unknown
}

public enum BatteryStatus
{
    [JsonPropertyName("available")]
    Available,
    [JsonPropertyName("unsupported")]
    Unsupported,
    [JsonPropertyName("unknown")]
    Unknown,
    [JsonPropertyName("error")]
    Error
}

public class DeviceBatteryInfo
{
    [JsonPropertyName("percentage")]
    public int? Percentage { get; set; }

    [JsonPropertyName("status")]
    public BatteryStatus Status { get; set; } = BatteryStatus.Unknown;

    [JsonPropertyName("charging")]
    public bool? Charging { get; set; }

    [JsonPropertyName("levelText")]
    public string? LevelText { get; set; }
}

public class DeviceError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class DeviceBatterySnapshot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public DeviceKind Kind { get; set; } = DeviceKind.Unknown;

    [JsonPropertyName("connection")]
    public DeviceConnection Connection { get; set; } = DeviceConnection.Unknown;

    [JsonPropertyName("battery")]
    public DeviceBatteryInfo Battery { get; set; } = new();

    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "cache";

    [JsonPropertyName("lastSeenAt")]
    public string LastSeenAt { get; set; } = DateTime.UtcNow.ToString("o");

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("error")]
    public DeviceError? Error { get; set; }
}
