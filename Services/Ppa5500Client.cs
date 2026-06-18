using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Newton4thGui.Services;

/// <summary>
/// RS232 client for Newtons4th PPA55xx series power analyzers.
/// Per PPA55xx user manual section 6:
///   - 8 data bits, no parity, 1 stop bit
///   - RTS/CTS flow control
///   - Both TX and RX terminator: carriage return ('\r') only - no line feed
///   - Commands case-insensitive, only first 6 chars significant
///   - *IDN? reply: company,product,serial,version
/// </summary>
public sealed class Ppa5500Client : IPpaClient
{
    private SerialPort? _port;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string PortName { get; private set; } = "COM5";
    public int BaudRate { get; private set; } = 19200;
    public bool IsOpen => _port?.IsOpen == true;
    public string? Identity { get; private set; }

    public void Open(string portName, int baudRate)
    {
        Close();
        PortName = portName;
        BaudRate = baudRate;
        // USB-CDC virtual COM (FTDI in the PPA5500) doesn't need hardware flow control
        // and write-timeouts when CTS isn't asserted. RS232 cable does, but we'll set
        // it back to None for both — the PPA5500's protocol uses '\r' as its only delimiter
        // and is self-pacing at 19200 baud.
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            ReadTimeout = 2000,
            WriteTimeout = 2000,
            NewLine = "\r",
            DtrEnable = true,
            RtsEnable = true,
        };
        _port.Open();

        // Device clear (Ctrl-T per manual section 6)
        _port.Write(new byte[] { 0x14 }, 0, 1);
        Thread.Sleep(200);
        _port.DiscardInBuffer();

        Identity = QueryRaw("*IDN?", 2000);
        // No automatic mode/display recovery — the user controls the unit
        // through the front panel; us forcing MODE,POWER or DISPLAY,N here
        // can put the instrument into a confused state if it doesn't
        // recognise the value, which produces shifted/garbled replies.
    }

    public void Close()
    {
        try { _port?.Close(); } catch { /* ignore */ }
        _port?.Dispose();
        _port = null;
        Identity = null;
    }

    public void Dispose() => Close();

    /// <summary>Send Ctrl-T device clear + drain. Resyncs the protocol after a timeout
    /// or short reply (which left a stale reply queued in the unit's TX buffer).</summary>
    public void Resync()
    {
        if (_port is null || !_port.IsOpen) return;
        try
        {
            _port.Write(new byte[] { 0x14 }, 0, 1);
            Thread.Sleep(200);
            _port.DiscardInBuffer();
        }
        catch { /* ignore */ }
    }

    private string QueryRaw(string cmd, int timeoutMs)
    {
        if (_port is null || !_port.IsOpen)
            throw new InvalidOperationException("Port is not open.");

        _port.DiscardInBuffer();
        _port.Write(cmd + "\r");

        var sb = new StringBuilder();
        var deadline = Environment.TickCount + timeoutMs;
        var oldTimeout = _port.ReadTimeout;
        bool gotTerminator = false;
        try
        {
            while (Environment.TickCount < deadline)
            {
                _port.ReadTimeout = Math.Max(50, deadline - Environment.TickCount);
                int b;
                try { b = _port.ReadByte(); }
                catch (TimeoutException) { continue; }
                if (b < 0) continue;
                if (b == '\r') { gotTerminator = true; break; }
                if (b == '\n') continue;
                sb.Append((char)b);
            }
        }
        finally
        {
            _port.ReadTimeout = oldTimeout;
        }

        if (!gotTerminator)
        {
            // Lost sync: send Ctrl-T to clear the unit's queued replies, then drain.
            // Without this, every subsequent query reads the previous query's late reply.
            try
            {
                _port.Write(new byte[] { 0x14 }, 0, 1);
                Thread.Sleep(150);
                _port.DiscardInBuffer();
            }
            catch { /* ignore */ }
        }
        else
        {
            // Tiny gap so the unit's command parser is ready for the next query.
            Thread.Sleep(15);
        }
        return sb.ToString();
    }

    public async Task<string> QueryAsync(string cmd, int timeoutMs = 2000, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => QueryRaw(cmd, timeoutMs), ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SendAsync(string cmd, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                if (_port is null || !_port.IsOpen) throw new InvalidOperationException("Port not open");
                _port.Write(cmd + "\r");
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Parses a comma-separated reply of scientific-notation floats.</summary>
    public static double[] ParseFloats(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return Array.Empty<double>();
        var parts = reply.Split(',');
        var result = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var t = parts[i].Trim();
            if (!double.TryParse(t, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out result[i]))
                result[i] = double.NaN;
        }
        return result;
    }
}
