using System;
using System.Globalization;

namespace Newton4thGui.Models;

/// <summary>One captured row, displayed in the Data Logger tab and written to CSV.</summary>
public sealed class LogRow
{
    public DateTime TimeLocal { get; init; }
    public double Frequency { get; init; }
    public double TempC { get; init; } = double.NaN;
    public double RhPct { get; init; } = double.NaN;
    public double Ch1Watts { get; init; }
    public double Ch2Watts { get; init; }
    public double Ch3Watts { get; init; }
    public double Ch1Vrms  { get; init; }
    public double Ch2Vrms  { get; init; }
    public double Ch3Vrms  { get; init; }
    public double Ch1Irms  { get; init; }
    public double Ch2Irms  { get; init; }
    public double Ch3Irms  { get; init; }
    public double Ch1Pf    { get; init; }
    public double Ch2Pf    { get; init; }
    public double Ch3Pf    { get; init; }
    public double Ch1Vthd  { get; init; }
    public double Ch2Vthd  { get; init; }
    public double Ch3Vthd  { get; init; }
    public double Ch1Ithd  { get; init; }
    public double Ch2Ithd  { get; init; }
    public double Ch3Ithd  { get; init; }

    public string TimeText => TimeLocal.ToString("yyyy-MM-dd HH:mm:ss");
    public string FreqText => Fmt(Frequency, 4);
    public string TempText => double.IsNaN(TempC) ? "-" : TempC.ToString("0.00", CultureInfo.InvariantCulture) + " °C";
    public string RhText   => double.IsNaN(RhPct) ? "-" : RhPct.ToString("0.0",  CultureInfo.InvariantCulture) + " %";
    public string Ch1WText => Eng(Ch1Watts);
    public string Ch2WText => Eng(Ch2Watts);
    public string Ch3WText => Eng(Ch3Watts);
    public string Ch1VText => Eng(Ch1Vrms);
    public string Ch2VText => Eng(Ch2Vrms);
    public string Ch3VText => Eng(Ch3Vrms);
    public string Ch1AText => Eng(Ch1Irms);
    public string Ch2AText => Eng(Ch2Irms);
    public string Ch3AText => Eng(Ch3Irms);
    public string Ch1PfText => Fmt(Ch1Pf, 4);
    public string Ch2PfText => Fmt(Ch2Pf, 4);
    public string Ch3PfText => Fmt(Ch3Pf, 4);
    public string Ch1VthdText => Eng(Ch1Vthd) + "%";
    public string Ch2VthdText => Eng(Ch2Vthd) + "%";
    public string Ch3VthdText => Eng(Ch3Vthd) + "%";
    public string Ch1IthdText => Eng(Ch1Ithd) + "%";
    public string Ch2IthdText => Eng(Ch2Ithd) + "%";
    public string Ch3IthdText => Eng(Ch3Ithd) + "%";

    public static LogRow FromSnapshot(PowerSnapshot s)
    {
        PhaseReading? P(int i) => i < s.Phases.Length ? s.Phases[i] : null;
        return new LogRow
        {
            TimeLocal = s.TimestampUtc.ToLocalTime(),
            Frequency = s.Frequency,
            TempC     = s.Env is { IsValid: true } ? s.Env.TempC : double.NaN,
            RhPct     = s.Env is { IsValid: true } ? s.Env.RhPct : double.NaN,
            Ch1Watts  = P(0)?.Watts ?? double.NaN,
            Ch2Watts  = P(1)?.Watts ?? double.NaN,
            Ch3Watts  = P(2)?.Watts ?? double.NaN,
            Ch1Vrms   = P(0)?.VRms  ?? double.NaN,
            Ch2Vrms   = P(1)?.VRms  ?? double.NaN,
            Ch3Vrms   = P(2)?.VRms  ?? double.NaN,
            Ch1Irms   = P(0)?.IRms  ?? double.NaN,
            Ch2Irms   = P(1)?.IRms  ?? double.NaN,
            Ch3Irms   = P(2)?.IRms  ?? double.NaN,
            Ch1Pf     = P(0)?.Pf    ?? double.NaN,
            Ch2Pf     = P(1)?.Pf    ?? double.NaN,
            Ch3Pf     = P(2)?.Pf    ?? double.NaN,
            Ch1Vthd   = P(0)?.VThdPercent ?? double.NaN,
            Ch2Vthd   = P(1)?.VThdPercent ?? double.NaN,
            Ch3Vthd   = P(2)?.VThdPercent ?? double.NaN,
            Ch1Ithd   = P(0)?.IThdPercent ?? double.NaN,
            Ch2Ithd   = P(1)?.IThdPercent ?? double.NaN,
            Ch3Ithd   = P(2)?.IThdPercent ?? double.NaN,
        };
    }

    private static string Fmt(double v, int dp)
        => double.IsNaN(v) ? "-" : v.ToString("0." + new string('0', dp), CultureInfo.InvariantCulture);

    private static string Eng(double v)
    {
        if (double.IsNaN(v)) return "-";
        if (v == 0) return "0.0000";
        var ci = CultureInfo.InvariantCulture;
        double a = Math.Abs(v);
        string sign = v < 0 ? "-" : "";
        double scaled; string suffix;
        if      (a >= 1e6)  { scaled = a / 1e6; suffix = "M"; }
        else if (a >= 1e3)  { scaled = a / 1e3; suffix = "k"; }
        else if (a >= 1)    { scaled = a;       suffix = "";  }
        else if (a >= 1e-3) { scaled = a * 1e3; suffix = "m"; }
        else if (a >= 1e-6) { scaled = a * 1e6; suffix = "µ"; }
        else if (a >= 1e-9) { scaled = a * 1e9; suffix = "n"; }
        else return v.ToString("0.000E+00", ci);

        string fmt = scaled >= 100 ? "0.00"
                   : scaled >= 10  ? "0.000"
                   :                 "0.0000";
        return sign + scaled.ToString(fmt, ci) + suffix;
    }
}
