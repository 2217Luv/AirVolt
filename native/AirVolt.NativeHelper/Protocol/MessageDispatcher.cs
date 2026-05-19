using AirVolt.NativeHelper.Providers;

namespace AirVolt.NativeHelper.Protocol;

public class MessageDispatcher
{
    private readonly Dictionary<string, Func<JsonRpcRequest, Task<JsonRpcResponse>>> _handlers = new();
    private readonly List<IDeviceBatteryProvider> _providers = new();

    public MessageDispatcher()
    {
        _handlers["helper.health"] = HandleHealth;
        _handlers["helper.version"] = HandleVersion;
        _handlers["devices.scan"] = HandleScan;
        _handlers["devices.watch.start"] = _ => Task.FromResult(Ok("not implemented"));
        _handlers["devices.watch.stop"] = _ => Task.FromResult(Ok("not implemented"));
    }

    public void AddProvider(IDeviceBatteryProvider provider)
    {
        _providers.Add(provider);
        _providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    public bool CanHandle(string method) => _handlers.ContainsKey(method);

    public Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request)
    {
        if (_handlers.TryGetValue(request.Method, out var handler))
        {
            return handler(request);
        }
        return Task.FromResult(Error(request.Id, "UNKNOWN_METHOD", $"Unknown method: {request.Method}"));
    }

    private Task<JsonRpcResponse> HandleHealth(JsonRpcRequest request)
    {
        return Task.FromResult(new JsonRpcResponse
        {
            Id = request.Id,
            Ok = true,
            Result = new { version = "0.1.0", providers = _providers.Select(p => p.Name).ToList() }
        });
    }

    private Task<JsonRpcResponse> HandleVersion(JsonRpcRequest request)
    {
        return Task.FromResult(new JsonRpcResponse
        {
            Id = request.Id,
            Ok = true,
            Result = new { version = "0.1.0" }
        });
    }

    private async Task<JsonRpcResponse> HandleScan(JsonRpcRequest request)
    {
        bool includeUnsupported = true;

        if (request.Params?.TryGetProperty("includeUnsupported", out var prop) == true)
        {
            includeUnsupported = prop.GetBoolean();
        }

        try
        {
            var allDevices = new List<Models.DeviceBatterySnapshot>();
            var seenIds = new HashSet<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasRealDevices = false;

            foreach (var provider in _providers)
            {
                // Skip mock provider if we already found real devices
                if (provider.Name == "mock" && hasRealDevices)
                {
                    Console.Error.WriteLine("[dispatcher] skipping mock provider — real devices found");
                    continue;
                }

                // If all devices already have battery data, skip remaining non-mock providers
                bool allHaveBattery = hasRealDevices &&
                    allDevices.Count > 0 &&
                    allDevices.All(d => d.Battery.Status == Models.BatteryStatus.Available);
                if (allHaveBattery && provider.Name != "mock")
                {
                    Console.Error.WriteLine($"[dispatcher] all {allDevices.Count} devices have battery — skipping {provider.Name}");
                    continue;
                }

                try
                {
                    var devices = await provider.ScanAsync();
                    foreach (var device in devices)
                    {
                        // If this device ID is new but name collides with an existing device,
                        // prefer the one with actual battery data (available > unknown/error)
                        if (!seenIds.Add(device.Id))
                        {
                            continue; // exact duplicate ID, skip
                        }

                        if (!seenNames.Add(device.Name))
                        {
                            // Name collision: replace the old entry if this one has better battery info
                            var existingIdx = allDevices.FindIndex(d =>
                                d.Name.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
                            if (existingIdx >= 0)
                            {
                                var existing = allDevices[existingIdx];
                                var newHasBattery = device.Battery.Status == Models.BatteryStatus.Available;
                                var oldHasBattery = existing.Battery.Status == Models.BatteryStatus.Available;
                                if (newHasBattery && !oldHasBattery)
                                {
                                    Console.Error.WriteLine(
                                        $"[dispatcher] replacing '{device.Name}' (unknown) with battery={device.Battery.Percentage}% from {device.Provider}");
                                    allDevices[existingIdx] = device;
                                }
                                else
                                {
                                    Console.Error.WriteLine(
                                        $"[dispatcher] skipping duplicate name '{device.Name}' from {device.Provider}");
                                }
                            }
                            continue;
                        }

                        allDevices.Add(device);
                    }
                    if (provider.Name != "mock" && devices.Count > 0)
                    {
                        hasRealDevices = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[dispatcher] provider {provider.Name} failed: {ex.Message}");
                }
            }

            if (!includeUnsupported)
            {
                allDevices.RemoveAll(d => d.Battery.Status == Models.BatteryStatus.Unsupported);
            }

            return new JsonRpcResponse
            {
                Id = request.Id,
                Ok = true,
                Result = new { devices = allDevices }
            };
        }
        catch (Exception ex)
        {
            return Error(request.Id, "SCAN_ERROR", ex.Message);
        }
    }

    private static JsonRpcResponse Ok(object? result = null)
    {
        return new JsonRpcResponse { Ok = true, Result = result };
    }

    private static JsonRpcResponse Error(string id, string code, string message)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Ok = false,
            Error = new JsonRpcError { Code = code, Message = message }
        };
    }
}
