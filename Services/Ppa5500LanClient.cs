using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Newton4thGui.Services;

/// <summary>
/// TCP/LAN client for Newtons4th PPA5xx series power analyzers (PPA530 on the
/// bench at 192.168.41.200). Wire protocol mirrors the RS232 transport:
///   - ASCII commands terminated with '\r' only
///   - replies comma-separated, terminated with '\r' (the LAN side may emit
///     trailing '\n' as well, which we silently swallow)
///   - Ctrl-T (0x14) is the device-clear / resync escape, same as serial
/// Single-client: the PPA accepts one socket at a time. Opening a second
/// session while one is active will typically time out.
/// </summary>
public sealed class Ppa5500LanClient : IPpaClient
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string Host { get; private set; } = "192.168.41.200";
    public int TcpPort { get; private set; } = 10001;
    public bool IsOpen => _tcp?.Connected == true && _stream is not null;
    public string? Identity { get; private set; }

    public void Open(string host, int port)
    {
        Close();
        Host = host;
        TcpPort = port;
        _tcp = new TcpClient
        {
            NoDelay = true,
            ReceiveTimeout = 2000,
            SendTimeout = 2000,
        };
        // Synchronous connect with a generous timeout so a wrong IP/port produces
        // a clear failure rather than blocking the UI thread indefinitely.
        var connectTask = _tcp.ConnectAsync(host, port);
        if (!connectTask.Wait(TimeSpan.FromSeconds(5)))
        {
            try { _tcp.Close(); } catch { /* ignore */ }
            _tcp = null;
            throw new TimeoutException($"Connect to {host}:{port} timed out");
        }
        _stream = _tcp.GetStream();
        _stream.ReadTimeout = 2000;
        _stream.WriteTimeout = 2000;

        // Device clear to flush anything left over from a previous session.
        _stream.Write(new byte[] { 0x14 }, 0, 1);
        Thread.Sleep(200);
        DrainAvailable();

        Identity = QueryRaw("*IDN?", 2000);
    }

    public void Close()
    {
        try { _stream?.Close(); } catch { /* ignore */ }
        try { _tcp?.Close(); }    catch { /* ignore */ }
        _stream = null;
        _tcp = null;
        Identity = null;
    }

    public void Dispose() => Close();

    public void Resync()
    {
        if (_stream is null) return;
        try
        {
            _stream.Write(new byte[] { 0x14 }, 0, 1);
            Thread.Sleep(200);
            DrainAvailable();
        }
        catch { /* ignore */ }
    }

    private void DrainAvailable()
    {
        if (_tcp is null || _stream is null) return;
        try
        {
            while (_tcp.Available > 0)
            {
                var buf = new byte[Math.Min(_tcp.Available, 4096)];
                _stream.Read(buf, 0, buf.Length);
            }
        }
        catch { /* ignore */ }
    }

    private string QueryRaw(string cmd, int timeoutMs)
    {
        if (_stream is null)
            throw new InvalidOperationException("Socket is not open.");

        DrainAvailable();
        var txBytes = Encoding.ASCII.GetBytes(cmd + "\r");
        _stream.Write(txBytes, 0, txBytes.Length);

        var sb = new StringBuilder();
        var deadline = Environment.TickCount + timeoutMs;
        bool gotTerminator = false;
        var oldTimeout = _stream.ReadTimeout;
        try
        {
            while (Environment.TickCount < deadline)
            {
                _stream.ReadTimeout = Math.Max(50, deadline - Environment.TickCount);
                int b;
                try { b = _stream.ReadByte(); }
                catch (IOException) { continue; }
                if (b < 0) continue;
                if (b == '\r') { gotTerminator = true; break; }
                if (b == '\n') continue;
                sb.Append((char)b);
            }
        }
        finally
        {
            _stream.ReadTimeout = oldTimeout;
        }

        if (!gotTerminator)
        {
            // Lost sync — clear the PPA's pending TX so the next query reads its
            // own reply, not the late tail of this one. Same fix as the RS232 path.
            try
            {
                _stream.Write(new byte[] { 0x14 }, 0, 1);
                Thread.Sleep(150);
                DrainAvailable();
            }
            catch { /* ignore */ }
        }
        else
        {
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
                if (_stream is null) throw new InvalidOperationException("Socket not open");
                var bytes = Encoding.ASCII.GetBytes(cmd + "\r");
                _stream.Write(bytes, 0, bytes.Length);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
