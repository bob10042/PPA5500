using System;

namespace Newton4thGui.Models;

/// <summary>One row of measurement data captured per poll cycle.</summary>
public sealed record PowerSnapshot
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public double Frequency { get; init; }
    public PhaseReading[] Phases { get; init; } = Array.Empty<PhaseReading>();

    public string RawRms { get; init; } = "";
    public string RawWva { get; init; } = "";
    public string RawPhPh { get; init; } = "";

    /// <summary>Latest ambient T/RH sample paired with this snapshot, if a Rotronic probe is connected.</summary>
    public EnvironmentSample? Env { get; init; }
}

public sealed class PhaseReading
{
    public int Phase { get; init; }

    // Direct-from-instrument
    public double VRms { get; init; }
    public double VDc { get; init; }
    public double IRms { get; init; }
    public double IDc { get; init; }
    public double Watts { get; init; }
    public double VPhPh { get; init; }   // phase-to-phase RMS

    // From HARMON,phase? (filled by harmonics polling)
    public double VThdPercent { get; init; }
    public double IThdPercent { get; init; }
    public double VPhase { get; init; }
    public double IPhase { get; init; }

    // Derived (match what the front panel shows)
    public double VA       => Math.Abs(VRms * IRms);
    public double VAr      => Math.Sqrt(Math.Max(0.0, VA * VA - Watts * Watts));
    public double Pf       => VA > 0 ? Watts / VA : 0.0;
    public double WattsDc  => VDc * IDc;
    public double DcPercent => Math.Abs(Watts) > 1e-12 ? (WattsDc / Watts) * 100.0 : 0.0;
}

