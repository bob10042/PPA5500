using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newton4thGui.Models;

namespace Newton4thGui.Services;

/// <summary>
/// Simple CSV row writer with per-write flush, suitable for long-running logs.
/// One row per sample, all 3 phases plus shared frequency.
/// Also writes a parallel NDJSON file (one JSON object per line) for structured
/// machine consumption / crash recovery.
/// </summary>
public sealed class CsvLogger : IDisposable
{
    private static readonly string[] PerPhaseCols =
    {
        "v_rms", "v_dc", "i_rms", "i_dc",
        "watts", "va", "var", "pf",
        "watts_dc", "dc_percent", "v_phph",
        "v_thd_pct", "i_thd_pct",
    };

    private StreamWriter? _writer;
    private StreamWriter? _jsonWriter;
    public string? FilePath { get; private set; }
    public string? JsonPath { get; private set; }
    public long RowsWritten { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public bool IsRunning => _writer is not null;

    public void Start(string folder)
    {
        Stop();
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        FilePath = Path.Combine(folder, $"PPA5500_log_{stamp}.csv");
        JsonPath = Path.Combine(folder, $"PPA5500_log_{stamp}.jsonl");

        _writer = new StreamWriter(FilePath, append: false) { AutoFlush = false };
        _writer.WriteLine(BuildHeader());
        _writer.Flush();

        _jsonWriter = new StreamWriter(JsonPath, append: false) { AutoFlush = false };
        // No header for NDJSON — each line is a self-describing object.

        RowsWritten = 0;
        StartedUtc = DateTime.UtcNow;
    }

    public void Stop()
    {
        try { _writer?.Flush();     _writer?.Dispose();     } catch { /* ignore */ }
        try { _jsonWriter?.Flush(); _jsonWriter?.Dispose(); } catch { /* ignore */ }
        _writer = null;
        _jsonWriter = null;
    }

    public void Dispose() => Stop();

    private static string BuildHeader()
    {
        var sb = new StringBuilder("date,time,freq_hz");
        for (int ch = 1; ch <= 3; ch++)
            foreach (var c in PerPhaseCols) sb.Append(',').Append("ch").Append(ch).Append('_').Append(c);
        return sb.ToString();
    }

    public void Write(PowerSnapshot snap)
    {
        if (_writer is null) return;
        var ci = CultureInfo.InvariantCulture;

        // ---- CSV ----
        var sb = new StringBuilder(256);
        var local = snap.TimestampUtc.ToLocalTime();
        sb.Append(local.ToString("yyyy-MM-dd", ci)).Append(',');
        sb.Append(local.ToString("HH:mm:ss.fff", ci)).Append(',');
        sb.Append(snap.Frequency.ToString("0.######", ci));

        for (int i = 0; i < 3; i++)
        {
            var p = i < snap.Phases.Length ? snap.Phases[i] : null;
            void Add(double v) => sb.Append(',').Append(p is null ? "" : v.ToString("0.######E+00", ci));
            if (p is null)
            {
                for (int k = 0; k < PerPhaseCols.Length; k++) sb.Append(',');
            }
            else
            {
                Add(p.VRms); Add(p.VDc); Add(p.IRms); Add(p.IDc);
                Add(p.Watts); Add(p.VA); Add(p.VAr); Add(p.Pf);
                Add(p.WattsDc); Add(p.DcPercent); Add(p.VPhPh);
                Add(p.VThdPercent); Add(p.IThdPercent);
            }
        }
        _writer.WriteLine(sb.ToString());
        _writer.Flush();

        // ---- NDJSON parallel write ----
        if (_jsonWriter is not null)
        {
            var js = new StringBuilder(384);
            js.Append('{');
            js.Append("\"date\":\"").Append(local.ToString("yyyy-MM-dd", ci)).Append("\",");
            js.Append("\"time\":\"").Append(local.ToString("HH:mm:ss.fff", ci)).Append("\",");
            js.Append("\"timestamp_utc\":\"").Append(snap.TimestampUtc.ToString("o", ci)).Append("\",");
            js.Append("\"freq_hz\":").Append(JF(snap.Frequency));
            for (int i = 0; i < 3; i++)
            {
                var p = i < snap.Phases.Length ? snap.Phases[i] : null;
                if (p is null) continue;
                js.Append(",\"ch").Append(i + 1).Append("\":{");
                js.Append("\"v_rms\":").Append(JF(p.VRms));
                js.Append(",\"v_dc\":").Append(JF(p.VDc));
                js.Append(",\"i_rms\":").Append(JF(p.IRms));
                js.Append(",\"i_dc\":").Append(JF(p.IDc));
                js.Append(",\"watts\":").Append(JF(p.Watts));
                js.Append(",\"va\":").Append(JF(p.VA));
                js.Append(",\"var\":").Append(JF(p.VAr));
                js.Append(",\"pf\":").Append(JF(p.Pf));
                js.Append(",\"watts_dc\":").Append(JF(p.WattsDc));
                js.Append(",\"dc_percent\":").Append(JF(p.DcPercent));
                js.Append(",\"v_phph\":").Append(JF(p.VPhPh));
                js.Append(",\"v_thd_pct\":").Append(JF(p.VThdPercent));
                js.Append(",\"i_thd_pct\":").Append(JF(p.IThdPercent));
                js.Append('}');
            }
            js.Append('}');
            _jsonWriter.WriteLine(js.ToString());
            _jsonWriter.Flush();
        }

        RowsWritten++;
    }

    private static string JF(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "null";
        return v.ToString("0.######E+00", CultureInfo.InvariantCulture);
    }
}
