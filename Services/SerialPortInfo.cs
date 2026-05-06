using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;

namespace Newton4thGui.Services;

/// <summary>One enumerable serial port with its Windows friendly description.</summary>
public sealed class SerialPortInfo
{
    public string PortName { get; init; } = "";       // e.g. "COM5"
    public string FriendlyName { get; init; } = "";   // e.g. "Prolific PL2303 GT USB Serial COM Port"
    public string DeviceId { get; init; } = "";       // e.g. "USB\\VID_067B&PID_23A3\\..."

    /// <summary>Best guess: which physical interface this represents on the PPA5500 setup.</summary>
    public InterfaceKind Kind => GuessKind(FriendlyName, DeviceId);

    /// <summary>"COM5 — Prolific PL2303 USB Serial COM Port [RS232 cable]"</summary>
    public string Display => string.IsNullOrWhiteSpace(FriendlyName)
        ? PortName
        : $"{PortName} — {FriendlyName} [{KindLabel(Kind)}]";

    public static IReadOnlyList<SerialPortInfo> Enumerate()
    {
        var byPort = new Dictionary<string, SerialPortInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT Name, Caption, DeviceID FROM Win32_PnPEntity " +
                "WHERE PNPClass = 'Ports' OR Name LIKE '%(COM%)%'");
            foreach (ManagementObject o in s.Get())
            {
                var name    = (o["Name"]    as string) ?? (o["Caption"] as string) ?? "";
                var caption = (o["Caption"] as string) ?? name;
                var deviceId= (o["DeviceID"]as string) ?? "";
                var port = ExtractComName(name) ?? ExtractComName(caption);
                if (port == null) continue;
                var friendly = name.Replace($"({port})", "").Trim();
                byPort[port] = new SerialPortInfo
                {
                    PortName = port,
                    FriendlyName = friendly,
                    DeviceId = deviceId,
                };
            }
        }
        catch { /* WMI not available — fall through to bare COM list */ }

        // Anything reported by SerialPort.GetPortNames but not enriched
        foreach (var p in SerialPort.GetPortNames())
        {
            if (!byPort.ContainsKey(p))
                byPort[p] = new SerialPortInfo { PortName = p, FriendlyName = "", DeviceId = "" };
        }

        return byPort.Values
            .OrderBy(v => int.TryParse(v.PortName.Replace("COM", ""), out var n) ? n : 999)
            .ToList();
    }

    private static string? ExtractComName(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        int open = text.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        int close = text.IndexOf(')', open);
        if (close < 0) return null;
        return text.Substring(open + 1, close - open - 1).Trim();
    }

    private static InterfaceKind GuessKind(string friendly, string deviceId)
    {
        var blob = (friendly + " " + deviceId).ToLowerInvariant();
        if (blob.Contains("prolific") || blob.Contains("pl2303"))   return InterfaceKind.Rs232Adapter;
        if (blob.Contains("ftdi") || blob.Contains("ft232") || blob.Contains("vid_0403"))
            return InterfaceKind.UsbInstrument;
        if (blob.Contains("newtons") || blob.Contains("n4l"))       return InterfaceKind.UsbInstrument;
        if (blob.Contains("silicon") || blob.Contains("cp210"))     return InterfaceKind.UsbInstrument;
        if (blob.Contains("usb"))                                    return InterfaceKind.UsbBridge;
        return InterfaceKind.Unknown;
    }

    private static string KindLabel(InterfaceKind k) => k switch
    {
        InterfaceKind.Rs232Adapter  => "RS232 cable",
        InterfaceKind.UsbInstrument => "USB",
        InterfaceKind.UsbBridge     => "USB-Serial",
        _ => "Serial",
    };
}

public enum InterfaceKind
{
    Unknown,
    Rs232Adapter,    // PL2303 / generic USB-RS232 adapter cable
    UsbBridge,       // Generic "USB Serial Device" with no further hint
    UsbInstrument,   // FTDI / SiLabs / Newtons4th-branded USB endpoint on the instrument itself
}
