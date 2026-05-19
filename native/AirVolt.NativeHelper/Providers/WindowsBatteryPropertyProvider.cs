using System.Diagnostics;
using System.Runtime.InteropServices;
using AirVolt.NativeHelper.Models;

namespace AirVolt.NativeHelper.Providers;

/// <summary>
/// Queries Windows PnP device property {104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2
/// which is where Windows stores Bluetooth device battery levels internally.
/// Uses CfgMgr32 CM_ API for device enumeration and property queries.
/// </summary>
public class WindowsBatteryPropertyProvider : IDeviceBatteryProvider
{
    public string Name => "windows-pnp-property";
    public int Priority => 5;

    private static readonly Guid BatteryPropertyGuid = new(0x104EA319, 0x6EE2, 0x4701, 0xBD, 0x47, 0x8D, 0xDB, 0xF4, 0x25, 0xBB, 0xE5);

    // DEVPKEY_Device_FriendlyName = {a45c254e-df1c-4efd-8020-67d146a850e0} 14
    private static readonly DEVPROPKEY DEVPKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
        pid = 14
    };

    private const uint CR_SUCCESS = 0;
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
    private const uint DEVPROP_TYPE_BYTE = 3; // DEVPROP_TYPE_BYTE = 0x03 in devpropdef.h
    private const uint DEVPROP_TYPE_STRING = 18; // DEVPROP_TYPE_STRING = 0x12 = 18

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("CfgMgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_SizeW(
        out uint pulLen,
        string? pszFilter,
        uint ulFlags);

    [DllImport("CfgMgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_ListW(
        string? pszFilter,
        IntPtr Buffer,
        uint BufferLen,
        uint ulFlags);

    [DllImport("CfgMgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(
        out uint pdnDevInst,
        string pDeviceID,
        uint ulFlags);

    [DllImport("CfgMgr32.dll", SetLastError = true)]
    private static extern uint CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DEVPROPKEY PropertyKey,
        out uint PropertyType,
        IntPtr PropertyBuffer,
        ref uint PropertyBufferSize,
        uint ulFlags);

    public Task<List<DeviceBatterySnapshot>> ScanAsync()
    {
        var results = new List<DeviceBatterySnapshot>();
        var now = DateTime.UtcNow.ToString("o");

        try
        {
            var batteryKey = new DEVPROPKEY { fmtid = BatteryPropertyGuid, pid = 2 };

            uint result = CM_Get_Device_ID_List_SizeW(out uint listSize, null, CM_GETIDLIST_FILTER_PRESENT);
            Console.Error.WriteLine($"[{Name}] device list size result={result}, charCount={listSize}");
            if (result != CR_SUCCESS || listSize == 0)
                return Task.FromResult(results);

            int bufSize = (int)((listSize + 1) * 2); // UTF-16 bytes + safety margin
            IntPtr buffer = Marshal.AllocHGlobal(bufSize);
            try
            {
                result = CM_Get_Device_ID_ListW(null, buffer, (uint)bufSize, CM_GETIDLIST_FILTER_PRESENT);
                if (result != CR_SUCCESS)
                {
                    Console.Error.WriteLine($"[{Name}] failed to get device list: {result}");
                    return Task.FromResult(results);
                }

                int btChecked = 0, batteryFound = 0;

                int offset = 0;
                while (offset < bufSize - 2)
                {
                    var ptr = IntPtr.Add(buffer, offset);
                    var deviceId = Marshal.PtrToStringUni(ptr);
                    if (string.IsNullOrEmpty(deviceId)) break;
                    offset += (deviceId.Length + 1) * 2;

                    if (!deviceId.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase) &&
                        !deviceId.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase))
                        continue;

                    btChecked++;

                    uint locateResult = CM_Locate_DevNodeW(out uint devInst, deviceId, 0);
                    if (locateResult != CR_SUCCESS) continue;

                    // Query battery property
                    uint propType;
                    uint propSize = 16;
                    IntPtr propBuffer = Marshal.AllocHGlobal(16);
                    try
                    {
                        uint propResult = CM_Get_DevNode_PropertyW(
                            devInst, ref batteryKey, out propType, propBuffer, ref propSize, 0);

                        if (propResult != CR_SUCCESS)
                            continue;

                        if (propType == 0) // DEVPROP_TYPE_EMPTY
                            continue;

                        if (propType != DEVPROP_TYPE_BYTE)
                        {
                            Console.Error.WriteLine($"[{Name}] unexpected battery prop type={propType} for {deviceId}");
                            continue;
                        }

                        byte[] data = new byte[propSize];
                        Marshal.Copy(propBuffer, data, 0, (int)propSize);
                        int batteryPercent = data[0];
                        if (batteryPercent < 0 || batteryPercent > 100) continue;

                        // Query friendly name
                        string? friendlyName = GetDevNodeStringProperty(devInst, DEVPKEY_Device_FriendlyName);
                        if (string.IsNullOrEmpty(friendlyName))
                        {
                            // Fallback: try to construct from InstanceId
                            friendlyName = TryGetNameFromInstanceId(deviceId);
                        }

                        if (string.IsNullOrEmpty(friendlyName))
                        {
                            Console.Error.WriteLine($"[{Name}] no name for {deviceId} (battery={batteryPercent}%)");
                            continue;
                        }

                        batteryFound++;
                        Console.Error.WriteLine($"[{Name}] #{batteryFound}: '{friendlyName}' → {batteryPercent}% [{deviceId}]");

                        // Clean up name
                        string baseName = friendlyName
                            .Replace(" Hands-Free AG", "")
                            .Replace(" Avrcp Transport", "")
                            .Replace(" Stereo", "")
                            .Replace(" Hands-Free AG Audio", "")
                            .Replace(" Avrcp Transport Audio", "")
                            .Trim();

                        if (string.IsNullOrEmpty(baseName)) continue;
                        if (baseName.StartsWith('{')) continue;
                        if (baseName.Equals("Bluetooth Device (RFCOMM Protocol TDI)", StringComparison.OrdinalIgnoreCase)) continue;

                        var kind = DeviceClassifier.Classify(baseName);

                        // Dedup by base name: prefer the first entry (this provider runs first)
                        if (results.Any(d => d.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        results.Add(new DeviceBatterySnapshot
                        {
                            Id = $"pnp:{deviceId}",
                            Name = baseName,
                            Kind = kind,
                            Connection = DeviceConnection.BluetoothClassic,
                            Battery = new DeviceBatteryInfo
                            {
                                Percentage = batteryPercent,
                                Status = BatteryStatus.Available,
                                LevelText = batteryPercent > 50 ? "high" : batteryPercent > 20 ? "medium" : "low",
                                Charging = false
                            },
                            Provider = Name,
                            LastSeenAt = now,
                            UpdatedAt = now
                        });
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(propBuffer);
                    }
                }

                Console.Error.WriteLine($"[{Name}] BT devices checked={btChecked}, battery found={batteryFound}");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Name}] scan error: {ex.GetType().Name}: {ex.Message}");
        }

        Console.Error.WriteLine($"[{Name}] total results: {results.Count}");
        return Task.FromResult(results);
    }

    private static string? GetDevNodeStringProperty(uint devInst, DEVPROPKEY key)
    {
        uint propType;
        uint propSize = 512;
        IntPtr propBuffer = Marshal.AllocHGlobal(512);
        try
        {
            uint result = CM_Get_DevNode_PropertyW(devInst, ref key, out propType, propBuffer, ref propSize, 0);
            if (result == CR_SUCCESS && propType == DEVPROP_TYPE_STRING && propSize > 0)
            {
                return Marshal.PtrToStringUni(propBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(propBuffer);
        }
        return null;
    }

    private static string? TryGetNameFromInstanceId(string deviceId)
    {
        // Try to extract a meaningful name from the InstanceId path
        // BTHLE\DEV_XXXXXXXXXXXX\... — just a MAC, not useful
        // BTHENUM\{UUID}_LOCALMFG&XXXX\...&MAC... — the UUID might map to a service name
        if (deviceId.Contains("0000111E", StringComparison.OrdinalIgnoreCase))
            return "Hands-Free AG"; // HFP
        if (deviceId.Contains("0000110C", StringComparison.OrdinalIgnoreCase))
            return "Avrcp Transport";
        if (deviceId.Contains("0000110E", StringComparison.OrdinalIgnoreCase))
            return "Avrcp Transport";
        if (deviceId.Contains("0000110B", StringComparison.OrdinalIgnoreCase))
            return "Stereo";
        return null;
    }
}
