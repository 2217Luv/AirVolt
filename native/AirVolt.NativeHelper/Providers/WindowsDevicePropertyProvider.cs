using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using System.Linq;
using AirVolt.NativeHelper.Models;

namespace AirVolt.NativeHelper.Providers;

public class WindowsDevicePropertyProvider : IDeviceBatteryProvider
{
    public string Name => "windows-device-property";
    public int Priority => 20;

    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelUuid = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");

    private static readonly HashSet<Guid> NonBatteryServiceUuids = new()
    {
        Guid.Parse("00001800-0000-1000-8000-00805f9b34fb"), // GAP
        Guid.Parse("00001801-0000-1000-8000-00805f9b34fb"), // GATT
        Guid.Parse("0000180a-0000-1000-8000-00805f9b34fb"), // Device Information
    };

    private static readonly string[] RequestedProperties =
    [
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.Bluetooth.Le.Appearance",
        "System.Devices.Aep.Bluetooth.Le.Appearance.Category",
        "System.Devices.Aep.Bluetooth.Le.IsConnectable",
        "System.Devices.Aep.IsPresent",
        "System.Devices.Aep.SignalStrength",
        "System.ItemNameDisplay"
    ];

    public async Task<List<DeviceBatterySnapshot>> ScanAsync()
    {
        var devices = new List<DeviceBatterySnapshot>();
        var now = DateTime.UtcNow.ToString("o");

        try
        {
            // Get paired Bluetooth devices
            var pairedSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            Console.Error.WriteLine($"[{Name}] querying paired BT devices...");
            var deviceInfos = await DeviceInformation.FindAllAsync(pairedSelector, RequestedProperties);
            Console.Error.WriteLine($"[{Name}] found {deviceInfos.Count} paired BT devices");

            foreach (var info in deviceInfos)
            {
                if (string.IsNullOrEmpty(info.Name)) continue;

                var device = await BuildDeviceSnapshotAsync(info, now);
                devices.Add(device);
            }

            // Also check connected non-paired devices
            var connectedSelector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(
                BluetoothConnectionStatus.Connected);
            Console.Error.WriteLine($"[{Name}] querying connected BT devices...");
            var connectedInfos = await DeviceInformation.FindAllAsync(connectedSelector, RequestedProperties);
            Console.Error.WriteLine($"[{Name}] found {connectedInfos.Count} connected BT devices");

            foreach (var info in connectedInfos)
            {
                if (string.IsNullOrEmpty(info.Name)) continue;
                if (devices.Any(d => d.Id == info.Id)) continue;

                var device = await BuildDeviceSnapshotAsync(info, now);
                devices.Add(device);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] scan error: {ex.GetType().Name}: {ex.Message}");
        }

        return devices;
    }

    private async Task<DeviceBatterySnapshot> BuildDeviceSnapshotAsync(DeviceInformation info, string now)
    {
        var kind = ClassifyDevice(info);
        var isConnected = GetBoolProperty(info, "System.Devices.Aep.IsConnected");
        var connection = DetectConnectionType(info);
        BatteryStatus status = BatteryStatus.Unknown;
        int? percentage = null;
        string? levelText = null;
        DeviceError? error = null;

        Console.Error.WriteLine($"[{Name}] device: {info.Name} kind={kind} connection={connection} connected={isConnected}");

        // Attempt BLE GATT battery read even for devices discovered via classic BT
        var batteryLevel = await TryReadBatteryForDeviceAsync(info);
        if (batteryLevel.HasValue)
        {
            percentage = batteryLevel.Value;
            status = BatteryStatus.Available;
            levelText = percentage > 50 ? "high" : percentage > 20 ? "medium" : "low";
            Console.Error.WriteLine($"[{Name}] {info.Name} battery={percentage}% (via GATT)");
        }
        else
        {
            Console.Error.WriteLine($"[{Name}] {info.Name} — no battery level (no standard BAS or GATT timeout)");
        }

        return new DeviceBatterySnapshot
        {
            Id = info.Id,
            Name = info.Name,
            Kind = kind,
            Connection = connection,
            Battery = new DeviceBatteryInfo
            {
                Percentage = percentage,
                Status = status,
                LevelText = levelText,
                Charging = false
            },
            Provider = Name,
            LastSeenAt = now,
            UpdatedAt = batteryLevel.HasValue ? now : (isConnected ? now : null),
            Error = error
        };
    }

    private async Task<int?> TryReadBatteryForDeviceAsync(DeviceInformation info)
    {
        BluetoothLEDevice? bleDevice = null;
        try
        {
            // Try by ID first (works for devices that already have a BLE DeviceInformation ID)
            try
            {
                bleDevice = await BluetoothLEDevice.FromIdAsync(info.Id);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{Name}] FromIdAsync failed for {info.Name}: {ex.Message}");
            }

            // Fallback: try by Bluetooth address
            if (bleDevice == null && TryGetBluetoothAddress(info, out var address))
            {
                try
                {
                    bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{Name}] FromBluetoothAddressAsync failed for {info.Name}: {ex.Message}");
                }
            }

            if (bleDevice == null) return null;

            return await ReadBatteryLevelFromDevice(bleDevice);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] GATT read error for {info.Name}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            bleDevice?.Dispose();
        }
    }

    private async Task<int?> ReadBatteryLevelFromDevice(BluetoothLEDevice bleDevice)
    {
        // Try cached first
        var level = await TryReadBatteryService(bleDevice, BluetoothCacheMode.Cached);
        if (level.HasValue) return level;

        // Fall back to uncached
        level = await TryReadBatteryService(bleDevice, BluetoothCacheMode.Uncached);
        if (level.HasValue) return level;

        // Last resort: scan all GATT characteristics for a battery-like value
        level = await TryDiscoverBatteryLevel(bleDevice, BluetoothCacheMode.Cached);
        if (level.HasValue) return level;

        return await TryDiscoverBatteryLevel(bleDevice, BluetoothCacheMode.Uncached);
    }

    private async Task<int?> TryReadBatteryService(BluetoothLEDevice bleDevice, BluetoothCacheMode cacheMode)
    {
        try
        {
            var gattResult = await bleDevice.GetGattServicesAsync(cacheMode);
            if (gattResult.Status != GattCommunicationStatus.Success) return null;

            GattDeviceService? batteryService = null;
            foreach (var service in gattResult.Services)
            {
                if (service.Uuid == BatteryServiceUuid) { batteryService = service; break; }
            }

            if (batteryService == null)
            {
                var serviceUuids = string.Join(", ", gattResult.Services.Select(s => s.Uuid.ToString("D")));
                Console.Error.WriteLine($"[{Name}] no Battery Service. Available: {serviceUuids}");
                return null;
            }

            var charResult = await batteryService.GetCharacteristicsAsync(cacheMode);
            if (charResult.Status != GattCommunicationStatus.Success) return null;

            GattCharacteristic? batteryChar = null;
            foreach (var characteristic in charResult.Characteristics)
            {
                if (characteristic.Uuid == BatteryLevelUuid) { batteryChar = characteristic; break; }
            }

            if (batteryChar == null) return null;

            var readResult = await batteryChar.ReadValueAsync(cacheMode);
            if (readResult.Status != GattCommunicationStatus.Success) return null;

            var reader = Windows.Storage.Streams.DataReader.FromBuffer(readResult.Value);
            return reader.ReadByte();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] GATT {cacheMode} error: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<int?> TryDiscoverBatteryLevel(
        BluetoothLEDevice bleDevice, BluetoothCacheMode cacheMode)
    {
        try
        {
            Console.Error.WriteLine($"[{Name}] discovering battery-like characteristics ({cacheMode})...");
            var gattResult = await bleDevice.GetGattServicesAsync(cacheMode);
            if (gattResult.Status != GattCommunicationStatus.Success) return null;

            var orderedServices = gattResult.Services
                .Where(s => !NonBatteryServiceUuids.Contains(s.Uuid))
                .OrderByDescending(s => IsCustomUuid(s.Uuid))
                .ThenBy(s => s.Uuid.ToString("D"))
                .ToList();

            foreach (var service in orderedServices)
            {
                try
                {
                    var charResult = await service.GetCharacteristicsAsync(cacheMode);
                    if (charResult.Status != GattCommunicationStatus.Success) continue;

                    foreach (var characteristic in charResult.Characteristics)
                    {
                        try
                        {
                            var readResult = await characteristic.ReadValueAsync(cacheMode);
                            if (readResult.Status != GattCommunicationStatus.Success) continue;

                            var data = readResult.Value;
                            if (data == null || data.Length == 0) continue;

                            var reader = DataReader.FromBuffer(data);
                            var bytes = new byte[reader.UnconsumedBufferLength];
                            reader.ReadBytes(bytes);

                            if (bytes.Length == 1 && bytes[0] <= 100)
                            {
                                Console.Error.WriteLine(
                                    $"[{Name}] discovered battery characteristic: " +
                                    $"service={service.Uuid:D} char={characteristic.Uuid:D} level={bytes[0]}%");
                                return bytes[0];
                            }

                            if (bytes.Length == 2 && bytes[0] <= 100 && bytes[1] <= 100
                                && (bytes[0] > 0 || bytes[1] > 0))
                            {
                                int level;
                                if (bytes[0] > 0 && bytes[1] > 0)
                                    level = (bytes[0] + bytes[1]) / 2;
                                else
                                    level = Math.Max(bytes[0], bytes[1]);

                                Console.Error.WriteLine(
                                    $"[{Name}] discovered dual battery characteristic: " +
                                    $"service={service.Uuid:D} char={characteristic.Uuid:D} " +
                                    $"b0={bytes[0]}% b1={bytes[1]}% result={level}%");
                                return level;
                            }

                            if (bytes.Length <= 4 && bytes.Length > 0)
                            {
                                var hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
                                Console.Error.WriteLine(
                                    $"[{Name}] characteristic " +
                                    $"service={service.Uuid:D} char={characteristic.Uuid:D} " +
                                    $"len={bytes.Length} data=[{hex}]");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine(
                                $"[{Name}] read char {characteristic.Uuid:D} error: {ex.GetType().Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[{Name}] enum service {service.Uuid:D} error: {ex.GetType().Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{Name}] discover {cacheMode} error: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }

    private static bool IsCustomUuid(Guid uuid)
    {
        var s = uuid.ToString("D");
        return !s.EndsWith("-0000-1000-8000-00805f9b34fb");
    }

    private static DeviceKind ClassifyDevice(DeviceInformation info)
    {
        var kind = DeviceClassifier.Classify(info.Name);
        if (kind != DeviceKind.Unknown) return kind;

        if (info.Properties.TryGetValue("System.Devices.Aep.Bluetooth.Le.Appearance.Category", out var categoryObj) && categoryObj != null)
        {
            try
            {
                var category = Convert.ToInt32(categoryObj);
                return category switch
                {
                    0x02 => DeviceKind.Mouse,
                    0x03 => DeviceKind.Keyboard,
                    0x08 => DeviceKind.Headset,
                    0x05 => DeviceKind.Controller,
                    _ => DeviceKind.Unknown
                };
            }
            catch { }
        }

        return DeviceKind.Unknown;
    }

    private static DeviceConnection DetectConnectionType(DeviceInformation info)
    {
        if (info.Properties.TryGetValue("System.Devices.Aep.Bluetooth.Le.IsConnectable", out var isLeObj) && isLeObj is true)
            return DeviceConnection.BluetoothLE;

        if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addrObj) && addrObj is string addr && !string.IsNullOrEmpty(addr))
        {
            if (addr.Length == 12 && addr.All(c => "0123456789ABCDEFabcdef".Contains(c)))
                return DeviceConnection.BluetoothClassic;
        }

        return DeviceConnection.BluetoothLE;
    }

    private static bool TryGetBluetoothAddress(DeviceInformation info, out ulong address)
    {
        address = 0;
        if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addrObj)
            && addrObj is string addrStr
            && !string.IsNullOrEmpty(addrStr))
        {
            try
            {
                address = Convert.ToUInt64(addrStr, 16);
                return true;
            }
            catch { }
        }

        // Fallback: parse MAC from device ID
        return TryGetBluetoothAddressFromDeviceId(info.Id, out address);
    }

    private static bool TryGetBluetoothAddressFromDeviceId(string deviceId, out ulong address)
    {
        address = 0;
        try
        {
            var parts = deviceId.Split(['#', '_', '-']);
            foreach (var part in parts)
            {
                var cleaned = part.Replace(":", "");
                if (cleaned.Length == 12 && cleaned.All(c =>
                    (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                {
                    address = Convert.ToUInt64(cleaned, 16);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool GetBoolProperty(DeviceInformation info, string key)
    {
        if (info.Properties.TryGetValue(key, out var value) && value is bool b)
            return b;
        return false;
    }
}
