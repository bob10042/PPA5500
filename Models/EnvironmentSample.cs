using System;

namespace Newton4thGui.Models;

/// <summary>
/// One ambient temperature / humidity sample, captured from a Rotronic Hygrolog
/// (or any other environmental probe later added). Logged alongside every
/// PowerSnapshot row so calibration paperwork can show the lab conditions
/// at the exact moment of each PPA reading.
/// </summary>
public sealed record EnvironmentSample
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public double TempC { get; init; } = double.NaN;
    public double RhPct { get; init; } = double.NaN;

    /// <summary>Short label of the probe / model / serial, e.g. "Rotronic HL20 sn 0020334732".</summary>
    public string Source { get; init; } = "";

    /// <summary>True if both values parsed successfully and within plausible ranges.</summary>
    public bool IsValid =>
        !double.IsNaN(TempC) && !double.IsNaN(RhPct) &&
        TempC > -50 && TempC < 100 &&
        RhPct >= 0 && RhPct <= 100;
}
