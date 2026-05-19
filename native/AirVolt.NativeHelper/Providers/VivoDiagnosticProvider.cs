using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using AirVolt.NativeHelper.Models;

namespace AirVolt.NativeHelper.Providers;

/// <summary>
/// Exhaustive diagnostic: search ALL Bluetooth devices (BLE + classic) for vivo.
/// Try connecting BLE device before GATT, and scan service-specific BLE selectors.
/// </summary>
public class VivoDiagnosticProvider : IDeviceBatteryProvider
{
    public string Name => "vivo-diag";
    public int Priority => 0;

    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb");

    public async Task<List<DeviceBatterySnapshot>> ScanAsync()
    {
        Console.Error.WriteLine("[vivo-diag] === Exhaustive device search ===");

        // 1. Search ALL BLE devices
        var bleSelector = BluetoothLEDevice.GetDeviceSelector();
        var bleDevices = await DeviceInformation.FindAllAsync(bleSelector);
        Console.Error.WriteLine($"[vivo-diag] ALL BLE devices: {bleDevices.Count}");
        foreach (var d in bleDevices)
        {
            Console.Error.WriteLine($"[vivo-diag]   BLE: '{d.Name}' id={d.Id}");
            if (d.Name?.Contains("vivo", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.Error.WriteLine($"[vivo-diag]     *** MATCH! ***");
                await ConnectAndDumpBleDevice(d.Id);
            }
        }

        // 2. Search BLE devices with Battery Service UUID
        var basSelector = GattDeviceService.GetDeviceSelectorFromUuid(BatteryServiceUuid);
        Console.Error.WriteLine("[vivo-diag] BLE devices with Battery Service...");
        var basDevices = await DeviceInformation.FindAllAsync(basSelector);
        Console.Error.WriteLine($"[vivo-diag] BAS devices: {basDevices.Count}");
        foreach (var d in basDevices)
        {
            Console.Error.WriteLine($"[vivo-diag]   BAS: '{d.Name}' id={d.Id}");
            if (d.Name?.Contains("vivo", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ConnectAndDumpBleDevice(d.Id);
            }
        }

        // 3. Search all paired BLE devices
        var blePairedSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var blePaired = await DeviceInformation.FindAllAsync(blePairedSelector);
        Console.Error.WriteLine($"[vivo-diag] Paired BLE devices: {blePaired.Count}");
        foreach (var d in blePaired)
        {
            Console.Error.WriteLine($"[vivo-diag]   Paired BLE: '{d.Name}' id={d.Id}");
            if (d.Name?.Contains("vivo", StringComparison.OrdinalIgnoreCase) == true)
            {
                await ConnectAndDumpBleDevice(d.Id);
            }
        }

        // 4. Search ALL classic devices
        var classicSelector = BluetoothDevice.GetDeviceSelector();
        var classicDevices = await DeviceInformation.FindAllAsync(classicSelector);
        Console.Error.WriteLine($"[vivo-diag] ALL classic BT devices: {classicDevices.Count}");
        foreach (var d in classicDevices)
        {
            Console.Error.WriteLine($"[vivo-diag]   classic: '{d.Name}' id={d.Id}");
            if (d.Name?.Contains("vivo", StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.Error.WriteLine($"[vivo-diag]     *** MATCH! ***");
                await DumpClassicAndBleDevice(d);
            }
        }

        Console.Error.WriteLine("[vivo-diag] === search complete ===");
        return new List<DeviceBatterySnapshot>();
    }

    /// <summary>
    /// Connect to a BLE device first, then dump GATT services.
    /// </summary>
    private static async Task ConnectAndDumpBleDevice(string deviceId)
    {
        BluetoothLEDevice? bleDevice = null;
        try
        {
            bleDevice = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (bleDevice == null)
            {
                Console.Error.WriteLine($"[vivo-diag]   FromIdAsync → null");
                return;
            }

            Console.Error.WriteLine($"[vivo-diag]   BLE: {bleDevice.Name}, Status={bleDevice.ConnectionStatus}, Addr=0x{bleDevice.BluetoothAddress:X12}");

            // If disconnected, try to connect
            if (bleDevice.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Console.Error.WriteLine("[vivo-diag]   attempting BLE connection...");
                try
                {
                    var connectResult = await bleDevice.RequestAccessAsync();
                    Console.Error.WriteLine($"[vivo-diag]   RequestAccess → {connectResult}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[vivo-diag]   connect error: {ex.Message}");
                }
                await Task.Delay(2000);
                Console.Error.WriteLine($"[vivo-diag]   after connect: Status={bleDevice.ConnectionStatus}");
            }

            await DumpGattServices(bleDevice);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[vivo-diag]   error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            bleDevice?.Dispose();
        }
    }

    private static async Task DumpClassicAndBleDevice(DeviceInformation info)
    {
        if (!TryParseAddress(info.Id, out var addr)) return;

        BluetoothLEDevice? bleDevice = null;
        try
        {
            bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[vivo-diag]   FromBluetoothAddressAsync error: {ex.Message}");
        }

        if (bleDevice == null)
        {
            Console.Error.WriteLine("[vivo-diag]   no BLE device for this address");
            return;
        }

        Console.Error.WriteLine($"[vivo-diag]   BLE: {bleDevice.Name}, Status={bleDevice.ConnectionStatus}");

        // Try to connect if disconnected
        if (bleDevice.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Console.Error.WriteLine("[vivo-diag]   attempting BLE connection...");
            try
            {
                var result = await bleDevice.RequestAccessAsync();
                Console.Error.WriteLine($"[vivo-diag]   RequestAccess → {result}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[vivo-diag]   RequestAccess error: {ex.Message}");
            }
            await Task.Delay(2000);
            Console.Error.WriteLine($"[vivo-diag]   after connect: Status={bleDevice.ConnectionStatus}");
        }

        await DumpGattServices(bleDevice);

        bleDevice.Dispose();
    }

    private static async Task DumpGattServices(BluetoothLEDevice bleDevice)
    {
        try
        {
            // Uncached first (critical for TWS)
            var gattResult = await bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            Console.Error.WriteLine($"[vivo-diag]   uncached GATT: {gattResult.Status}, {gattResult.Services.Count} services");

            foreach (var svc in gattResult.Services)
            {
                try
                {
                    var chars = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (chars.Status != GattCommunicationStatus.Success) continue;

                    foreach (var ch in chars.Characteristics)
                    {
                        var props = ch.CharacteristicProperties;
                        Console.Error.WriteLine($"[vivo-diag]     svc={svc.Uuid:D} char={ch.Uuid:D} props={props}");

                        if ((props & GattCharacteristicProperties.Read) != 0)
                        {
                            try
                            {
                                var read = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                                if (read.Status == GattCommunicationStatus.Success && read.Value != null && read.Value.Length > 0)
                                {
                                    var reader = DataReader.FromBuffer(read.Value);
                                    var bytes = new byte[reader.UnconsumedBufferLength];
                                    reader.ReadBytes(bytes);
                                    var hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
                                    var vals = string.Join(", ", bytes.Select(b => (int)b));
                                    Console.Error.WriteLine($"[vivo-diag]       READ[{bytes.Length}]: [{vals}] {hex}");
                                }
                                else
                                {
                                    Console.Error.WriteLine($"[vivo-diag]       read status={read.Status}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[vivo-diag]       read err: {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[vivo-diag]   GATT error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool TryParseAddress(string deviceId, out ulong addr)
    {
        addr = 0;
        try
        {
            var parts = deviceId.Split(['#', '_', '-']);
            foreach (var part in parts)
            {
                var cleaned = part.Replace(":", "");
                if (cleaned.Length == 12 && cleaned.All(c =>
                    (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                {
                    addr = Convert.ToUInt64(cleaned, 16);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }
}
