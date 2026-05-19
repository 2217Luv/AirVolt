using AirVolt.NativeHelper.Models;

namespace AirVolt.NativeHelper.Providers;

public interface IDeviceBatteryProvider
{
    string Name { get; }
    int Priority { get; }
    Task<List<DeviceBatterySnapshot>> ScanAsync();
}
