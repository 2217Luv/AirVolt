using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;
using System.Linq;
using System.Threading;
using AirVolt.NativeHelper.Models;

namespace AirVolt.NativeHelper.Providers;

public class BluetoothBasProvider : IDeviceBatteryProvider
{
    public string Name => "bluetooth-bas";
    public int Priority => 10;

    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelUuid = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");

    private const int GattTimeoutMs = 1500;
    private const int GattDiscoveryTimeoutMs = 2500;

    // Standard GATT services that are NOT battery services
    private static readonly HashSet<Guid> NonBatteryServiceUuids = new()
    {
        Guid.Parse("00001800-0000-1000-8000-00805f9b34fb"), // GAP
        Guid.Parse("00001801-0000-1000-8000-00805f9b34fb"), // GATT
        Guid.Parse("0000180a-0000-1000-8000-00805f9b34fb"), // Device Information
    };

    // Known vendor-proprietary services that are NOT battery-related.
    // These use the Broadcom/Qualcomm base UUID (d102-11e1-9b23-00025b00a5a5)
    // and carry command/control protocols (GAIA, firmware, etc.), not battery data.
    private static readonly HashSet<Guid> NonBatteryVendorServiceUuids = new()
    {
        Guid.Parse("00001100-d102-11e1-9b23-00025b00a5a5"), // GAIA command protocol
        Guid.Parse("0000eb10-d102-11e1-9b23-00025b00a5a5"), // Qualcomm proprietary
    };

    public async Task<List<DeviceBatterySnapshot>> ScanAsync()
    {
        var devices = new List<DeviceBatterySnapshot>();
        var seenAddresses = new HashSet<ulong>();
        var now = DateTime.UtcNow.ToString("o");

        // Step 1: Enumerate paired BLE devices
        await ScanBleDevices(
            BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
            devices, seenAddresses, now);

        // Step 1b: Also enumerate connected BLE devices (some BLE devices connect
        // without being paired as BLE, especially dual-mode headsets)
        await ScanBleDevices(
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            devices, seenAddresses, now);

        // Step 2: Enumerate classic Bluetooth devices (paired) and try BLE GATT via MAC address
        await ScanClassicBluetoothDevices(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            devices, seenAddresses, now);

        // Step 3: Also check connected (but not necessarily paired) classic devices
        await ScanClassicBluetoothDevices(
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            devices, seenAddresses, now);

        return devices;
    }

    private async Task ScanBleDevices(
        string bleSelector,
        List<DeviceBatterySnapshot> devices, HashSet<ulong> seenAddresses, string now)
    {
        try
        {
            Console.Error.WriteLine($"[{Name}] querying BLE devices...");
            var deviceInfos = await DeviceInformation.FindAllAsync(bleSelector);
            Console.Error.WriteLine($"[{Name}] found {deviceInfos.Count} BLE devices");

            foreach (var info in deviceInfos)
            {
                if (string.IsNullOrEmpty(info.Name)) continue;

                // Skip if already found by a previous BLE scan
                if (devices.Any(d => d.Id == info.Id)) continue;

                Console.Error.WriteLine($"[{Name}] BLE device: {info.Name}");

                var device = new DeviceBatterySnapshot
                {
                    Id = info.Id,
                    Name = info.Name,
                    Kind = DeviceClassifier.Classify(info.Name),
                    Connection = DeviceConnection.BluetoothLE,
                    Battery = new DeviceBatteryInfo
                    {
                        Percentage = null,
                        Status = BatteryStatus.Unknown
                    },
                    Provider = Name,
                    LastSeenAt = now
                };

                try
                {
                    var batteryLevel = await ReadBatteryLevelWithTimeout(info.Id);
                    if (batteryLevel.HasValue)
                    {
                        device.Battery.Percentage = batteryLevel.Value;
                        device.Battery.Status = BatteryStatus.Available;
                        device.Battery.LevelText = batteryLevel.Value > 50 ? "high" :
                                                   batteryLevel.Value > 20 ? "medium" : "low";
                        device.UpdatedAt = now;
                        Console.Error.WriteLine($"[{Name}] {info.Name} battery={batteryLevel.Value}%");
                    }
                    else
                    {
                        device.Battery.Status = BatteryStatus.Unknown;
                        Console.Error.WriteLine($"[{Name}] {info.Name} no battery level (timeout or no BAS)");
                    }
                }
                catch (Exception ex)
                {
                    device.Battery.Status = BatteryStatus.Unknown;
                    device.Error = new DeviceError
                    {
                        Code = "GATT_NO_BATTERY_SERVICE",
                        Message = "Device does not expose standard BLE Battery Service"
                    };
                    Console.Error.WriteLine($"[{Name}] {info.Name} GATT error: {ex.Message}");
                }

                devices.Add(device);

                if (TryGetBluetoothAddressFromBleId(info.Id, out var addr))
                {
                    seenAddresses.Add(addr);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] BLE paired scan error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task ScanClassicBluetoothDevices(
        string selector,
        List<DeviceBatterySnapshot> devices, HashSet<ulong> seenAddresses, string now)
    {
        try
        {
            string[] requestedProps = { "System.Devices.Aep.DeviceAddress" };
            Console.Error.WriteLine($"[{Name}] querying classic BT devices with selector...");
            var deviceInfos = await DeviceInformation.FindAllAsync(selector, requestedProps);
            Console.Error.WriteLine($"[{Name}] found {deviceInfos.Count} classic BT devices");

            foreach (var info in deviceInfos)
            {
                if (string.IsNullOrEmpty(info.Name)) continue;

                Console.Error.WriteLine($"[{Name}] classic device: {info.Name}");

                // Dedup by MAC address
                if (!TryGetBluetoothAddress(info, out var address))
                {
                    // Fallback: dedup by device name (BLE scan might have already found this device)
                    if (devices.Any(d => d.Name.Equals(info.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.Error.WriteLine($"[{Name}] {info.Name} — already seen via BLE scan, skipping (name match)");
                        continue;
                    }
                }
                else if (!seenAddresses.Add(address))
                {
                    Console.Error.WriteLine($"[{Name}] {info.Name} — already seen via BLE scan, skipping (MAC match)");
                    continue;
                }

                var kind = DeviceClassifier.Classify(info.Name);
                var connection = DeviceConnection.BluetoothClassic;

                BluetoothLEDevice? bleDevice = null;
                try
                {
                    Console.Error.WriteLine($"[{Name}] {info.Name} — looking up BLE by address (0x{address:X12})...");
                    bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{Name}] {info.Name} — FromBluetoothAddressAsync failed: {ex.Message}");
                }

                // Fallback: try to find BLE device by name match (some dual-mode devices
                // use a different BD_ADDR for BLE vs BR/EDR, e.g. vivo TWS, OPPO Enco)
                if (bleDevice == null)
                {
                    bleDevice = await FindBleDeviceByNameAsync(info.Name);
                }

                if (bleDevice != null)
                {
                    // Give Windows a moment to enumerate GATT services on the device.
                    // Without this, GetGattServicesAsync may return incomplete results.
                    await Task.Delay(500);

                    try
                    {
                        var batteryLevel = await ReadBatteryLevelFromDeviceWithDiscovery(bleDevice);
                        var device = new DeviceBatterySnapshot
                        {
                            Id = info.Id,
                            Name = info.Name,
                            Kind = kind,
                            Connection = connection,
                            Battery = new DeviceBatteryInfo
                            {
                                Percentage = batteryLevel,
                                Status = batteryLevel.HasValue ? BatteryStatus.Available : BatteryStatus.Unknown
                            },
                            Provider = Name,
                            LastSeenAt = now,
                            UpdatedAt = batteryLevel.HasValue ? now : null
                        };

                        if (batteryLevel.HasValue)
                        {
                            device.Battery.LevelText = batteryLevel.Value > 50 ? "high" :
                                                       batteryLevel.Value > 20 ? "medium" : "low";
                            Console.Error.WriteLine($"[{Name}] {info.Name} battery={batteryLevel.Value}% (via classic→BLE)");
                        }
                        else
                        {
                            Console.Error.WriteLine($"[{Name}] {info.Name} — BLE found but no Battery Service");
                        }

                        devices.Add(device);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[{Name}] {info.Name} — GATT read failed: {ex.Message}");
                        AddClassicOnlyDevice(devices, info, kind, connection, now);
                    }
                    finally
                    {
                        bleDevice.Dispose();
                    }
                }
                else
                {
                    Console.Error.WriteLine($"[{Name}] {info.Name} — no BLE counterpart, adding as classic-only");
                    AddClassicOnlyDevice(devices, info, kind, connection, now);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] classic BT scan error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AddClassicOnlyDevice(
        List<DeviceBatterySnapshot> devices, DeviceInformation info,
        DeviceKind kind, DeviceConnection connection, string now)
    {
        // Don't assume classic — the device was found via classic selector but
        // may actually support BLE (many dual-mode devices pair as classic BT).
        var effectiveConnection = connection == DeviceConnection.BluetoothClassic
            ? DeviceConnection.Unknown
            : connection;

        devices.Add(new DeviceBatterySnapshot
        {
            Id = info.Id,
            Name = info.Name,
            Kind = kind,
            Connection = effectiveConnection,
            Battery = new DeviceBatteryInfo
            {
                Percentage = null,
                Status = BatteryStatus.Unknown
            },
            Provider = "bluetooth-bas",
            LastSeenAt = now
        });
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

        // Fallback: parse MAC from the device ID itself.
        // Classic BT IDs look like: Bluetooth#Bluetooth<adapterMAC>-<deviceMAC>
        // e.g. Bluetooth#Bluetoothc8:15:4e:c8:b1:66-68:62:8a:a7:38:24
        //       → device MAC = 68:62:8a:a7:38:24 → 0x68628AA73824
        return TryGetBluetoothAddressFromDeviceId(info.Id, out address);
    }

    private static bool IsCustomUuid(Guid uuid)
    {
        // Standard Bluetooth SIG UUIDs use the base: 0000xxxx-0000-1000-8000-00805f9b34fb
        // Custom vendor UUIDs use a different pattern (e.g. 2587db3c-ce70-4fc9-935f-777ab4188fd7)
        var s = uuid.ToString("D");
        return !s.EndsWith("-0000-1000-8000-00805f9b34fb");
    }

    private static bool TryGetBluetoothAddressFromDeviceId(string deviceId, out ulong address)
    {
        address = 0;
        try
        {
            // Device IDs contain MAC addresses separated by #, _, or -
            // Examples:
            //   BLE:    BluetoothLE#BluetoothLEc8:15:4e:c8:b1:66-ac:8e:bd:51:6c:ee
            //   Classic: Bluetooth#Bluetoothc8:15:4e:c8:b1:66-68:62:8a:a7:38:24
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

    private static bool TryGetBluetoothAddressFromBleId(string bleDeviceId, out ulong address)
    {
        address = 0;
        try
        {
            var parts = bleDeviceId.Split(['#', '_', '-']);
            foreach (var part in parts)
            {
                var cleaned = part.Replace(":", "");
                if (cleaned.Length == 12 && cleaned.All(c => "0123456789ABCDEFabcdef".Contains(c)))
                {
                    address = Convert.ToUInt64(cleaned, 16);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private async Task<int?> ReadBatteryLevelWithTimeout(string deviceId)
    {
        // First try fast standard BAS read (3s timeout)
        using var cts = new CancellationTokenSource(GattTimeoutMs);
        try
        {
            var task = ReadBatteryLevelById(deviceId);
            var completed = await Task.WhenAny(task, Task.Delay(GattTimeoutMs, cts.Token));
            if (completed == task) { cts.Cancel(); var result = await task; if (result.HasValue) return result; }
        }
        catch (OperationCanceledException) { }

        // Standard BAS failed — try discovery with longer timeout
        using var cts2 = new CancellationTokenSource(GattDiscoveryTimeoutMs);
        try
        {
            var task = ReadBatteryLevelByIdWithDiscovery(deviceId);
            var completed = await Task.WhenAny(task, Task.Delay(GattDiscoveryTimeoutMs, cts2.Token));
            if (completed == task) { cts2.Cancel(); return await task; }
            cts2.Cancel();
            try { await task; } catch (OperationCanceledException) { }
        }
        catch (OperationCanceledException) { }

        return null;
    }

    private async Task<int?> ReadBatteryLevelById(string deviceId)
    {
        BluetoothLEDevice? bleDevice = null;
        try
        {
            bleDevice = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (bleDevice == null) return null;
            return await ReadBatteryLevelFromDevice(bleDevice);
        }
        finally { bleDevice?.Dispose(); }
    }

    private async Task<int?> ReadBatteryLevelByIdWithDiscovery(string deviceId)
    {
        BluetoothLEDevice? bleDevice = null;
        try
        {
            bleDevice = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (bleDevice == null) return null;
            await Task.Delay(500);
            return await ReadBatteryLevelFromDeviceWithDiscovery(bleDevice);
        }
        finally
        {
            if (bleDevice != null)
            {
                try { bleDevice.Dispose(); } catch { }
            }
        }
    }

    private static async Task<int?> ReadBatteryLevelFromDevice(BluetoothLEDevice bleDevice)
    {
        // Try cached BAS first (fast, works for Xbox controllers, keyboards, etc.)
        var level = await TryReadBatteryService(bleDevice, BluetoothCacheMode.Cached);
        if (level.HasValue) return level;

        // TWS earbuds like vivo only expose characteristics during the FIRST uncached
        // GATT enumeration — subsequent calls return empty characteristic lists.
        // Combine BAS check + discovery in a single uncached pass.
        return await ReadBatteryLevelSinglePass(bleDevice);
    }

    private static async Task<int?> ReadBatteryLevelSinglePass(BluetoothLEDevice bleDevice)
    {
        try
        {
            var gattResult = await bleDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (gattResult.Status != GattCommunicationStatus.Success) return null;

            // Check standard BAS first
            foreach (var service in gattResult.Services)
            {
                if (service.Uuid != BatteryServiceUuid) continue;

                var charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (charResult.Status != GattCommunicationStatus.Success) break;

                foreach (var ch in charResult.Characteristics)
                {
                    if (ch.Uuid != BatteryLevelUuid) continue;
                    var readResult = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (readResult.Status == GattCommunicationStatus.Success && readResult.Value != null)
                    {
                        var reader = DataReader.FromBuffer(readResult.Value);
                        if (reader.UnconsumedBufferLength > 0)
                            return reader.ReadByte();
                    }
                }
                break;
            }

            // No standard BAS — scan all services for battery-like characteristics.
            // Collect ALL candidates then pick the best one (don't return early —
            // there may be multiple battery-related characteristics like earbud vs case).
            var candidates = new List<(Guid ServiceUuid, Guid CharUuid, int Level)>();

            var orderedServices = gattResult.Services
                .Where(s => !NonBatteryServiceUuids.Contains(s.Uuid))
                .OrderByDescending(s => IsCustomUuid(s.Uuid))
                .ThenBy(s => s.Uuid.ToString("D"))
                .ToList();

            foreach (var service in orderedServices)
            {
                try
                {
                    var charResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (charResult.Status != GattCommunicationStatus.Success) continue;

                    foreach (var ch in charResult.Characteristics)
                    {
                        if ((ch.CharacteristicProperties & GattCharacteristicProperties.Read) == 0)
                            continue;

                        try
                        {
                            var readResult = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (readResult.Status != GattCommunicationStatus.Success) continue;

                            var data = readResult.Value;
                            if (data == null || data.Length == 0) continue;

                            var reader = DataReader.FromBuffer(data);
                            var bytes = new byte[reader.UnconsumedBufferLength];
                            reader.ReadBytes(bytes);

                            var level = ParseBatteryBytes(bytes);
                            if (level.HasValue)
                            {
                                Console.Error.WriteLine(
                                    $"[bluetooth-bas] battery candidate: " +
                                    $"service={service.Uuid:D} char={ch.Uuid:D} level={level.Value}%");
                                candidates.Add((service.Uuid, ch.Uuid, level.Value));
                            }

                            if (bytes.Length <= 4)
                            {
                                var hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
                                Console.Error.WriteLine(
                                    $"[bluetooth-bas] char {service.Uuid:D}/{ch.Uuid:D} " +
                                    $"data[{bytes.Length}]: {hex}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine(
                                $"[bluetooth-bas] read char {ch.Uuid:D} error: {ex.GetType().Name}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[bluetooth-bas] enum service {service.Uuid:D} error: {ex.GetType().Name}");
                }
            }

            if (candidates.Count > 0)
            {
                // Prefer the highest battery value (more likely to be the primary one).
                // For dual-battery earbuds, the lower value might be 0 (not-in-use earbud)
                // while the higher matches the system reading.
                var best = candidates.OrderByDescending(c => c.Level).First();
                Console.Error.WriteLine(
                    $"[bluetooth-bas] selected best candidate: service={best.ServiceUuid:D} " +
                    $"char={best.CharUuid:D} level={best.Level}% " +
                    $"(from {candidates.Count} candidates)");
                return best.Level;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[bluetooth-bas] single-pass GATT error: {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    private async Task<int?> ReadBatteryLevelFromDeviceWithDiscovery(BluetoothLEDevice bleDevice)
    {
        // Single-pass uncached GATT — the only reliable approach for TWS earbuds
        // that stop responding to GATT after the first enumeration.
        var level = await ReadBatteryLevelFromDevice(bleDevice);
        if (level.HasValue) return level;

        // Notification-based discovery — last resort for devices that only push
        // battery data via notifications (don't support direct reads at all).
        using (var cts = new CancellationTokenSource(GattDiscoveryTimeoutMs))
        {
            try
            {
                var task = TryDiscoverBatteryLevelViaNotification(bleDevice, BluetoothCacheMode.Cached, cts.Token);
                var completed = await Task.WhenAny(task, Task.Delay(GattDiscoveryTimeoutMs, cts.Token));
                if (completed == task) { var result = await task; if (result.HasValue) return result; }
                cts.Cancel();
                try { await task; } catch (OperationCanceledException) { }
            }
            catch (OperationCanceledException) { }
        }

        return null;
    }

    private static async Task<BluetoothLEDevice?> FindBleDeviceByNameAsync(string name)
    {
        try
        {
            Console.Error.WriteLine($"[bluetooth-bas] searching BLE devices by name: \"{name}\"...");
            // Search ALL BLE devices regardless of pairing/connection state
            var selector = BluetoothLEDevice.GetDeviceSelector();
            var deviceInfos = await DeviceInformation.FindAllAsync(selector);

            foreach (var info in deviceInfos)
            {
                if (string.IsNullOrEmpty(info.Name)) continue;
                if (!info.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                Console.Error.WriteLine($"[bluetooth-bas] found BLE match by name: {info.Name} (id={info.Id})");
                try
                {
                    var bleDevice = await BluetoothLEDevice.FromIdAsync(info.Id);
                    if (bleDevice != null) return bleDevice;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[bluetooth-bas] FromIdAsync failed for {info.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bluetooth-bas] BLE name search error: {ex.Message}");
        }
        return null;
    }

    private async Task<int?> TryDiscoverBatteryLevelViaNotification(
        BluetoothLEDevice bleDevice, BluetoothCacheMode cacheMode, CancellationToken ct = default)
    {
        try
        {
            Console.Error.WriteLine($"[bluetooth-bas] notification discovery ({cacheMode})...");

            var gattResult = await bleDevice.GetGattServicesAsync(cacheMode);
            if (gattResult.Status != GattCommunicationStatus.Success) return null;
            if (ct.IsCancellationRequested) return null;

            var candidates = new List<(GattDeviceService Service, GattCharacteristic Characteristic)>();

            foreach (var service in gattResult.Services)
            {
                if (NonBatteryServiceUuids.Contains(service.Uuid)) continue;
                if (NonBatteryVendorServiceUuids.Contains(service.Uuid)) continue;
                if (ct.IsCancellationRequested) return null;

                try
                {
                    var charResult = await service.GetCharacteristicsAsync(cacheMode);
                    if (charResult.Status != GattCommunicationStatus.Success) continue;

                    foreach (var ch in charResult.Characteristics)
                    {
                        var props = ch.CharacteristicProperties;
                        if ((props & (GattCharacteristicProperties.Notify | GattCharacteristicProperties.Indicate)) != 0)
                        {
                            candidates.Add((service, ch));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[bluetooth-bas]   enum service {service.Uuid:D} error: {ex.GetType().Name}");
                }
            }

            if (candidates.Count == 0)
            {
                Console.Error.WriteLine("[bluetooth-bas] no notify-capable characteristics");
                return null;
            }

            candidates = candidates
                .OrderByDescending(c => IsCustomUuid(c.Service.Uuid))
                .ThenBy(c => c.Service.Uuid.ToString("D"))
                .ToList();

            Console.Error.WriteLine(
                $"[bluetooth-bas] subscribing to {candidates.Count} notify-capable characteristics...");

            var notificationTcs = new TaskCompletionSource<byte[]?>();
            var subscriptions = new List<(GattCharacteristic Char, TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> Handler)>();

            using var timeoutCts = new CancellationTokenSource(2000);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var reg = linkedCts.Token.Register(() => notificationTcs.TrySetResult(null));

            foreach (var (service, ch) in candidates)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var capturedServiceUuid = service.Uuid;
                    var capturedCharUuid = ch.Uuid;

                    TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = (sender, args) =>
                    {
                        try
                        {
                            var reader = DataReader.FromBuffer(args.CharacteristicValue);
                            var bytes = new byte[reader.UnconsumedBufferLength];
                            reader.ReadBytes(bytes);

                            var hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
                            Console.Error.WriteLine(
                                $"[bluetooth-bas] notify: svc={capturedServiceUuid:D} " +
                                $"char={capturedCharUuid:D} data[{bytes.Length}]: {hex}");

                            var level = ParseBatteryBytes(bytes);
                            if (level.HasValue)
                            {
                                Console.Error.WriteLine(
                                    $"[bluetooth-bas] notify battery: {level.Value}%");
                                notificationTcs.TrySetResult(bytes);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine(
                                $"[bluetooth-bas] notify handler error: {ex.Message}");
                        }
                    };

                    ch.ValueChanged += handler;
                    subscriptions.Add((ch, handler));

                    var cccdValue = (ch.CharacteristicProperties & GattCharacteristicProperties.Notify) != 0
                        ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                        : GattClientCharacteristicConfigurationDescriptorValue.Indicate;

                    var cccdResult = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

                    if (cccdResult == GattCommunicationStatus.Success)
                    {
                        Console.Error.WriteLine(
                            $"[bluetooth-bas]   subscribed: svc={service.Uuid:D} char={ch.Uuid:D}");
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"[bluetooth-bas]   CCCD failed ({cccdResult}): svc={service.Uuid:D} char={ch.Uuid:D}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[bluetooth-bas]   subscribe fail char={ch.Uuid:D}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            try
            {
                var resultBytes = await notificationTcs.Task;
                if (resultBytes != null)
                {
                    return ParseBatteryBytes(resultBytes);
                }
                Console.Error.WriteLine("[bluetooth-bas] notification wait — no battery data");
            }
            finally
            {
                foreach (var (ch, handler) in subscriptions)
                {
                    try
                    {
                        ch.ValueChanged -= handler;
                        await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                    }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[bluetooth-bas] notification discovery cancelled");
        }
        catch (ObjectDisposedException)
        {
            Console.Error.WriteLine("[bluetooth-bas] notification discovery aborted — device disposed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[bluetooth-bas] notification discovery error: {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    private static int? ParseBatteryBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return null;

        // Reject data with any byte outside the 0–100 range — battery percentages
        // never exceed 100, so bytes > 100 indicate non-battery data.
        if (bytes.Any(b => b > 100)) return null;

        if (bytes.Length == 1 && bytes[0] <= 100)
            return bytes[0];

        if (bytes.Length == 2 && bytes[0] <= 100 && bytes[1] <= 100
            && (bytes[0] > 0 || bytes[1] > 0))
        {
            if (bytes[0] > 0 && bytes[1] > 0)
                return (bytes[0] + bytes[1]) / 2;
            return Math.Max(bytes[0], bytes[1]);
        }

        return null;
    }

    private static async Task<int?> TryReadBatteryService(BluetoothLEDevice bleDevice, BluetoothCacheMode cacheMode)
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
                // Log all services for diagnostics
                var serviceUuids = string.Join(", ", gattResult.Services.Select(s => s.Uuid.ToString("D")));
                Console.Error.WriteLine($"[bluetooth-bas] no Battery Service on device. Available services: {serviceUuids}");
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
            Console.Error.WriteLine($"[bluetooth-bas] GATT {cacheMode} error: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
