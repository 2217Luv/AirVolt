using AirVolt.NativeHelper.Models;

namespace AirVolt.NativeHelper.Providers;

public class MockDeviceProvider : IDeviceBatteryProvider
{
    public string Name => "mock";
    public int Priority => 99;

    public Task<List<DeviceBatterySnapshot>> ScanAsync()
    {
        var now = DateTime.UtcNow.ToString("o");
        var devices = new List<DeviceBatterySnapshot>
        {
            new()
            {
                Id = "mock-bt-mouse-01",
                Name = "MX Master 3S",
                Kind = DeviceKind.Mouse,
                Connection = DeviceConnection.BluetoothLE,
                Battery = new DeviceBatteryInfo
                {
                    Percentage = 78,
                    Status = BatteryStatus.Available,
                    Charging = false,
                    LevelText = "high"
                },
                Provider = "bluetooth-bas",
                LastSeenAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "mock-bt-keyboard-01",
                Name = "Keychron K3 Pro",
                Kind = DeviceKind.Keyboard,
                Connection = DeviceConnection.BluetoothLE,
                Battery = new DeviceBatteryInfo
                {
                    Percentage = 45,
                    Status = BatteryStatus.Available,
                    Charging = false,
                    LevelText = "medium"
                },
                Provider = "bluetooth-bas",
                LastSeenAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "mock-bt-headset-01",
                Name = "Sony WH-1000XM5",
                Kind = DeviceKind.Headset,
                Connection = DeviceConnection.BluetoothClassic,
                Battery = new DeviceBatteryInfo
                {
                    Percentage = 12,
                    Status = BatteryStatus.Available,
                    Charging = false,
                    LevelText = "low"
                },
                Provider = "windows-device-property",
                LastSeenAt = now,
                UpdatedAt = now
            },
            new()
            {
                Id = "mock-dongle-keyboard-01",
                Name = "G915 Keyboard",
                Kind = DeviceKind.Keyboard,
                Connection = DeviceConnection.Usb24G,
                Battery = new DeviceBatteryInfo
                {
                    Percentage = null,
                    Status = BatteryStatus.Unsupported
                },
                Provider = "cache",
                LastSeenAt = now,
                UpdatedAt = null
            },
            new()
            {
                Id = "mock-controller-01",
                Name = "Xbox Wireless Controller",
                Kind = DeviceKind.Controller,
                Connection = DeviceConnection.Usb,
                Battery = new DeviceBatteryInfo
                {
                    Percentage = 90,
                    Status = BatteryStatus.Available,
                    Charging = true,
                    LevelText = "high"
                },
                Provider = "hid",
                LastSeenAt = now,
                UpdatedAt = now
            }
        };

        return Task.FromResult(devices);
    }
}
