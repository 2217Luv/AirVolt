using AirVolt.NativeHelper.Protocol;
using AirVolt.NativeHelper.Providers;

var dispatcher = new MessageDispatcher();

// Register providers in priority order (lower = higher priority)
// 1. Windows PnP battery property — authoritative (uses system battery data)
dispatcher.AddProvider(new WindowsBatteryPropertyProvider());
// 2. BLE GATT Battery Service — fallback for BLE devices
dispatcher.AddProvider(new BluetoothBasProvider());
// 3. Mock data — development fallback (skipped when real devices found)
dispatcher.AddProvider(new MockDeviceProvider());

Console.Error.WriteLine("[helper] AirVolt NativeHelper v0.1.0 ready");

while (true)
{
    var line = Console.ReadLine();
    if (line == null) break; // stdin closed

    var request = JsonRpcSerializer.DeserializeRequest(line);
    if (request == null)
    {
        Console.Error.WriteLine($"[helper] failed to parse: {line}");
        continue;
    }

    Console.Error.WriteLine($"[helper] received method={request.Method} id={request.Id}");

    try
    {
        var response = await dispatcher.DispatchAsync(request);
        var json = JsonRpcSerializer.SerializeResponse(response);
        Console.Error.WriteLine($"[helper] sending response id={response.Id} ok={response.Ok} len={json.Length}");
        Console.Out.WriteLine(json);
        Console.Out.Flush();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[helper] dispatch error: {ex.Message}");
        var errorResponse = new JsonRpcResponse
        {
            Id = request.Id,
            Ok = false,
            Error = new JsonRpcError { Code = "INTERNAL", Message = ex.Message }
        };
        Console.Out.WriteLine(JsonRpcSerializer.SerializeResponse(errorResponse));
        Console.Out.Flush();
    }
}
