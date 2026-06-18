using System;
using System.Threading;
using System.Threading.Tasks;

namespace Newton4thGui.Services;

/// <summary>
/// Transport-neutral interface for talking to a Newtons4th PPA5xx power analyzer.
/// Implemented by Ppa5500Client (RS232 / USB-CDC) and Ppa5500LanClient (TCP).
/// Wire protocol is identical across transports per PPA55xx user manual section 6:
/// ASCII commands terminated with '\r', comma-separated replies, Ctrl-T (0x14)
/// device-clear for resync.
/// </summary>
public interface IPpaClient : IDisposable
{
    bool IsOpen { get; }
    string? Identity { get; }
    void Close();
    void Resync();
    Task<string> QueryAsync(string cmd, int timeoutMs = 2000, CancellationToken ct = default);
    Task SendAsync(string cmd, CancellationToken ct = default);
}
