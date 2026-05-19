namespace AirVolt.NativeHelper.Models;

public static class DeviceClassifier
{
    public static DeviceKind Classify(string name)
    {
        var lower = name.ToLowerInvariant();

        // Mouse
        if (MatchAny(lower, "mouse", "mx master", "mx anywhere", "g502", "g pro", "g903",
            "deathadder", "basilisk", "viper", "g305", "g603", "g604", "g703", "g900",
            "m720", "m590", "m585", "m337", "m331", "m221", "m185", "m170",
            "orochi", "naga", "lancehead", "mamba"))
            return DeviceKind.Mouse;

        // Keyboard
        if (MatchAny(lower, "keyboard", "keychron", "infi75", "g915", "g613", "k855",
            "anne pro", "ducky", "filco", "leopold", "varmilo", "ikbc",
            "nuphy", "lofree", "epomaker", "royal kludge", "rk ", "redragon",
            "k3 ", "k8 ", "q1 ", "q3 ", "k2 ", "k4 ", "k6 ", "k10", "k12", "k14"))
            return DeviceKind.Keyboard;

        // Headset / Earbuds
        if (MatchAny(lower, "headset", "headphone", "earbud", "earphone",
            "wh-1000", "wf-1000", "wh-ch", "wf-c",
            "airpod", "airpods", "galaxy bud", "tws",
            "sanag", "vivo tws", "oppo enco", "xiaomi bud", "huawei freebud",
            "soundcore", "jbl", "bose", "sennheiser", "beats",
            "sony wh", "sony wf", "qc35", "qc45", "px7", "px8",
            "momentum", "hd 4.50", "hd 450"))
            return DeviceKind.Headset;

        // Controller
        if (MatchAny(lower, "controller", "gamepad", "xbox", "dualsense", "dualshock",
            "joy-con", "pro controller", "8bitdo", "stratus", "nimbus"))
            return DeviceKind.Controller;

        // Pen
        if (MatchAny(lower, "pen", "stylus", "pencil", "surface slim pen"))
            return DeviceKind.Pen;

        return DeviceKind.Unknown;
    }

    public static DeviceConnection DetectConnection(string protocol, bool isBLE)
    {
        if (isBLE) return DeviceConnection.BluetoothLE;
        if (protocol.Contains("bluetooth", StringComparison.OrdinalIgnoreCase))
            return DeviceConnection.BluetoothClassic;
        if (protocol.Contains("usb", StringComparison.OrdinalIgnoreCase))
            return DeviceConnection.Usb;
        return DeviceConnection.Unknown;
    }

    private static bool MatchAny(string lowerName, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (lowerName.Contains(pattern, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
