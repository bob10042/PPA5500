using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FTD2XX_NET;
using Microsoft.Win32;
using Newton4thGui.Models;

namespace Newton4thGui.Services;

/// <summary>
/// Driver for Rotronic HygroLog HL-series probes over FTDI D2XX.
///
/// Talks the same protocol as Rotronic's own HW4 software — direct to the
/// embedded FTDI bridge by USB iSerial, no virtual COM port required (and
/// none should be enabled: HW4 expects the chip in D2XX-only mode).
///
/// Verified equivalent of <c>rotronic_hygrolog.py</c> in this project:
///   * 19200 8N1, no flow control
///   * Command frame: <c>{ 99RDD }\r\n</c>
///   * Response is semicolon-separated; field layout fixed by the HW4 spec
///     (see field-index constants below)
///   * Settle time ~600 ms between TX and RX queue check
/// </summary>
public sealed class RotronicClient : IDisposable
{
    private const int    DefaultBaud      = 19200;
    private const string RotronicBusDesc  = "Rotronic USB Interface";
    private static readonly byte[] RddCommand =
        Encoding.ASCII.GetBytes("{ 99RDD }\r\n");

    // Field indices in the semicolon-separated RDD response.
    private const int RddFieldRh     = 1;
    private const int RddFieldT      = 5;
    private const int RddFieldFw     = 15;
    private const int RddFieldSerial = 16;
    private const int RddFieldModel  = 17;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private FTDI? _ft;
    private string _usbISerial = "";
    private string _model = "";
    private string _deviceSerial = "";
    private string _firmware = "";

    public bool IsOpen => _ft is { IsOpen: true };
    public string DeviceLabel
    {
        get
        {
            if (!IsOpen) return "(not connected)";
            var model = string.IsNullOrEmpty(_model) ? "Rotronic" : _model;
            return string.IsNullOrEmpty(_deviceSerial)
                ? $"{model} (USB {_usbISerial})"
                : $"{model} sn {_deviceSerial}";
        }
    }

    /// <summary>
    /// Find the FTDI serial number of the Rotronic by reading the Windows PnP
    /// registry. We can't rely on FTD2XX_NET's GetDeviceList (the 1.0.14 build
    /// returns empty Description/SerialNumber strings on .NET 8 — a known
    /// marshalling bug), and the registry-stored BusReportedDeviceDesc is
    /// empty under the Device Parameters key (Windows surfaces it via a
    /// DEVPKEY GUID property store, not a flat REG_SZ value).
    ///
    /// Heuristic that works without any of that: FTDI chips with a factory
    /// programmed iSerial appear under
    ///   HKLM\...\Enum\USB\VID_0403&amp;PID_6001\&lt;ALNUM_SERIAL&gt;
    /// whereas FTDIs with no programmed EEPROM serial get a Windows-synthesised
    /// instance ID built from the USB bus topology — these always contain '&amp;'
    /// (e.g. <c>5&amp;2699FB5D&amp;0&amp;2</c>). Rotronic factory-programs every probe
    /// with an alnum iSerial; the PPA5500's internal FTDI has no EEPROM serial.
    /// So: skip subkeys containing '&amp;', and (when multiple are present) prefer
    /// one with the Rotronic factory prefix "RO".
    /// </summary>
    private static string? DiscoverRotronicSerial()
    {
        const string root = @"SYSTEM\CurrentControlSet\Enum\USB\VID_0403&PID_6001";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(root);
            if (key is null) return null;

            string? best = null;
            foreach (var serial in key.GetSubKeyNames())
            {
                if (serial.Contains('&')) continue;       // Windows-synthesised, skip
                if (string.IsNullOrWhiteSpace(serial)) continue;
                if (serial.StartsWith("RO", StringComparison.Ordinal))
                    return serial;                         // Rotronic factory prefix — done
                best ??= serial;                          // fallback if no "RO" match
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Open the Rotronic FTDI by its USB iSerial, discovered via the Windows
    /// PnP registry (since the FTD2XX_NET 1.0.14 EEPROM-string reads are broken
    /// on .NET 8). Returns false if no Rotronic-flagged FTDI is present.
    /// </summary>
    public bool TryOpen()
    {
        Close();

        var serial = DiscoverRotronicSerial();
        if (serial is null) return false;

        var ft = new FTDI();
        if (ft.OpenBySerialNumber(serial) != FTDI.FT_STATUS.FT_OK)
            return false;

        var ok =
            ft.SetBaudRate(DefaultBaud) == FTDI.FT_STATUS.FT_OK &&
            ft.SetDataCharacteristics(FTDI.FT_DATA_BITS.FT_BITS_8,
                                      FTDI.FT_STOP_BITS.FT_STOP_BITS_1,
                                      FTDI.FT_PARITY.FT_PARITY_NONE) == FTDI.FT_STATUS.FT_OK &&
            ft.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE, 0, 0) == FTDI.FT_STATUS.FT_OK &&
            ft.SetTimeouts(500, 500) == FTDI.FT_STATUS.FT_OK &&
            ft.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX) == FTDI.FT_STATUS.FT_OK;

        if (!ok)
        {
            ft.Close();
            return false;
        }

        _ft = ft;
        _usbISerial = serial;
        return true;
    }

    public void Close()
    {
        try { _ft?.Close(); } catch { /* ignore */ }
        _ft = null;
        _usbISerial = "";
        _model = "";
        _deviceSerial = "";
        _firmware = "";
    }

    public void Dispose()
    {
        Close();
        _lock.Dispose();
    }

    /// <summary>
    /// Issue one RDD query and return a parsed <see cref="EnvironmentSample"/>.
    /// Throws on protocol failure; caller decides whether to retry, log, or
    /// surface a stale sample.
    /// </summary>
    public async Task<EnvironmentSample> ReadAsync(int settleMs = 600, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ft is null || !_ft.IsOpen)
                throw new InvalidOperationException("Rotronic device is not open.");

            _ft.Purge(FTDI.FT_PURGE.FT_PURGE_RX | FTDI.FT_PURGE.FT_PURGE_TX);

            uint written = 0;
            var w = _ft.Write(RddCommand, RddCommand.Length, ref written);
            if (w != FTDI.FT_STATUS.FT_OK || written != RddCommand.Length)
                throw new InvalidOperationException($"RDD write failed (status={w}, written={written}).");

            await Task.Delay(settleMs, ct).ConfigureAwait(false);

            uint avail = 0;
            _ft.GetRxBytesAvailable(ref avail);
            if (avail == 0)
                throw new InvalidOperationException("No response from Rotronic within settle window.");

            var buf = new byte[avail];
            uint read = 0;
            var r = _ft.Read(buf, avail, ref read);
            if (r != FTDI.FT_STATUS.FT_OK || read == 0)
                throw new InvalidOperationException($"RDD read failed (status={r}, read={read}).");

            // Latin-1 (instead of strict ASCII) tolerates the °C byte (0xB0) that
            // the unit emits in the temperature-unit field.
            var text = Encoding.GetEncoding("ISO-8859-1")
                               .GetString(buf, 0, (int)read)
                               .Trim()
                               .TrimStart('{')
                               .TrimEnd();
            var fields = text.Split(';');
            if (fields.Length <= RddFieldModel)
                throw new InvalidOperationException($"Short RDD frame ({fields.Length} fields).");

            var rh = ParseFloat(fields[RddFieldRh]);
            var t  = ParseFloat(fields[RddFieldT]);

            // Refresh cached device info every read — cheap and self-healing if
            // the probe was unplugged-and-replugged into the same hub slot.
            _firmware     = fields[RddFieldFw].Trim();
            _deviceSerial = fields[RddFieldSerial].Trim();
            _model        = fields[RddFieldModel].Trim();

            return new EnvironmentSample
            {
                TimestampUtc = DateTime.UtcNow,
                TempC = t,
                RhPct = rh,
                Source = DeviceLabel,
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    private static double ParseFloat(string token)
    {
        var s = token.Trim();
        if (string.IsNullOrEmpty(s)) return double.NaN;
        // The unit emits "---.-" for "no measurement available".
        bool allDashSpaceDot = true;
        foreach (var c in s) { if (c != '-' && c != '.' && c != ' ') { allDashSpaceDot = false; break; } }
        if (allDashSpaceDot) return double.NaN;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : double.NaN;
    }
}
