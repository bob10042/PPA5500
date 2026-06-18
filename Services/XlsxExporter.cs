using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace Newton4thGui.Services;

/// <summary>
/// Exports a PPA5500/PPA530 CSV log to a slimmed-down XLSX for the proteomics
/// calibration paperwork. Columns: Day, Date, Time, Temp (°C), RH (%), V1, V2,
/// V3, Freq (Hz), plus a Min/Max/Average summary grid (Temp, Hum, Volt, Freq)
/// and the logged date range off to the right. The full CSV stays on disk
/// untouched so the raw data is always available.
/// </summary>
public static class XlsxExporter
{
    private const string HeaderNavy = "FF0F172A";
    private const string MetaLight  = "FFE2E8F0";
    private const string V1Light    = "FFDBEAFE";  // blue   for V1
    private const string V2Light    = "FFD1FAE5";  // green  for V2
    private const string V3Light    = "FFFED7AA";  // orange for V3

    /// <summary>Convert a CSV file to a minimal XLSX. Returns the output path.</summary>
    public static string Convert(string csvPath, string? outputPath = null)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("CSV not found", csvPath);

        outputPath ??= Path.ChangeExtension(csvPath, ".xlsx");

        var rows = ReadCsv(csvPath);
        if (rows.Count == 0) throw new InvalidDataException("CSV is empty");

        var header = rows[0];
        var idx = MapColumns(header);
        if (idx.Date < 0 || idx.Time < 0)
            throw new InvalidDataException("CSV is missing date/time columns — was this written by the current CsvLogger?");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("PPA Log");

        // Title row
        var title = ws.Cell(1, 1);
        title.Value = "Power Analyzer Log — proteomics export";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        title.Style.Font.FontColor = XLColor.FromArgb(15, 23, 42);

        var src = ws.Cell(1, 5);
        src.Value = "Source: " + Path.GetFileName(csvPath);
        src.Style.Font.Italic = true;
        src.Style.Font.FontSize = 10;
        src.Style.Font.FontColor = XLColor.FromArgb(100, 116, 139);
        ws.Row(1).Height = 22;

        // Column headers at row 3
        string[] outHeaders = { "Day", "Date", "Time", "Temp (°C)", "RH (%)", "V1 (V)", "V2 (V)", "V3 (V)", "Freq (Hz)" };
        for (int i = 0; i < outHeaders.Length; i++)
        {
            var cell = ws.Cell(3, i + 1);
            cell.Value = outHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 11;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(203, 213, 225);
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderNavy);
        }
        ws.Row(3).Height = 30;

        // Data rows from row 4
        int dataRowCount = 0;
        string? firstDate = null, lastDate = null;   // feeds the "Date from / Date to" summary cells
        for (int r = 1; r < rows.Count; r++)
        {
            var src2 = rows[r];
            int xlRow = 4 + dataRowCount;

            string dateRaw = idx.Date < src2.Length ? src2[idx.Date] : "";
            string timeRaw = idx.Time < src2.Length ? src2[idx.Time] : "";
            if (string.IsNullOrWhiteSpace(dateRaw) && string.IsNullOrWhiteSpace(timeRaw)) continue;

            // Track the chronological span. ISO yyyy-MM-dd sorts lexicographically,
            // so ordinal min/max gives the true range regardless of row order.
            if (!string.IsNullOrWhiteSpace(dateRaw))
            {
                if (firstDate is null || string.CompareOrdinal(dateRaw, firstDate) < 0) firstDate = dateRaw;
                if (lastDate  is null || string.CompareOrdinal(dateRaw, lastDate)  > 0) lastDate  = dateRaw;
            }

            // Day-of-week from the parsed date; empty if the date cell is unparseable.
            string day = "";
            if (DateTime.TryParseExact(dateRaw, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                day = d.ToString("ddd", CultureInfo.InvariantCulture);
            }

            WriteText(ws.Cell(xlRow, 1), day,     MetaLight, bold: false);
            WriteText(ws.Cell(xlRow, 2), dateRaw, MetaLight, bold: true);
            WriteText(ws.Cell(xlRow, 3), timeRaw, MetaLight, bold: true);

            WriteNumber(ws.Cell(xlRow, 4), Get(src2, idx.Temp), null,    "0.00");
            WriteNumber(ws.Cell(xlRow, 5), Get(src2, idx.Rh),   null,    "0.0");
            WriteNumber(ws.Cell(xlRow, 6), Get(src2, idx.V1),   V1Light, "0.00");
            WriteNumber(ws.Cell(xlRow, 7), Get(src2, idx.V2),   V2Light, "0.00");
            WriteNumber(ws.Cell(xlRow, 8), Get(src2, idx.V3),   V3Light, "0.00");
            WriteNumber(ws.Cell(xlRow, 9), Get(src2, idx.Freq), null,    "0.0000");

            dataRowCount++;
        }

        // Widths
        ws.Column(1).Width = 7;   // Day
        ws.Column(2).Width = 12;  // Date
        ws.Column(3).Width = 13;  // Time
        ws.Column(4).Width = 11;  // Temp
        ws.Column(5).Width = 9;   // RH
        ws.Column(6).Width = 11;  // V1
        ws.Column(7).Width = 11;  // V2
        ws.Column(8).Width = 11;  // V3
        ws.Column(9).Width = 12;  // Freq

        // Freeze header + the date/day so scrolling keeps context.
        ws.SheetView.FreezeRows(3);
        ws.SheetView.FreezeColumns(3);

        if (dataRowCount > 0)
        {
            var range = ws.Range(3, 1, 3 + dataRowCount, outHeaders.Length);
            range.SetAutoFilter();

            // Min/Max/Average + date-range summary, to the right of the data block.
            WriteSummary(ws, firstDate, lastDate);
        }

        wb.SaveAs(outputPath);
        return outputPath;
    }

    private static string Get(string[] row, int i) => (i >= 0 && i < row.Length) ? row[i] : "";

    private static void WriteText(IXLCell cell, string val, string bgColorHtml, bool bold)
    {
        cell.Value = val;
        cell.Style.Font.FontName = "Consolas";
        cell.Style.Font.Bold = bold;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(bgColorHtml);
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
    }

    private static void WriteNumber(IXLCell cell, string raw, string? bgColorHtml, string fmt)
    {
        cell.Style.Font.FontName = "Consolas";
        cell.Style.Font.FontSize = 10;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv)
            && !double.IsNaN(fv))
        {
            cell.Value = fv;
            cell.Style.NumberFormat.Format = fmt;
        }
        else
        {
            cell.Value = "";
        }

        if (bgColorHtml is not null)
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(bgColorHtml);
    }

    /// <summary>
    /// Writes the Min/Max/Average summary grid (Temp, Hum, Volt, Freq) and the
    /// Date-from/Date-to range to the right of the data block, mirroring the
    /// layout used on the calibration paperwork:
    ///
    ///            L          M(Temp)        N(Hum)         O(Volt)        P(Freq)
    ///   row1                Temp           Hum            Volt           Freq      (+ J1/K1 date labels)
    ///   row2    Min         =MIN(D:D)      =MIN(E:E)      =MIN(F:F)      =MIN(I:I)
    ///   row3    Max         =MAX(D:D)      =MAX(E:E)      =MAX(F:F)      =MAX(I:I)
    ///   row4    Average     =AVERAGE(D:D)  =AVERAGE(E:E)  =AVERAGE(F:F)  =AVERAGE(I:I)
    ///
    /// Whole-column references match the source paperwork and are safe: MIN/MAX/
    /// AVERAGE ignore the text header in row 3 and the blank cells above it.
    /// </summary>
    private static void WriteSummary(IXLWorksheet ws, string? firstDate, string? lastDate)
    {
        const int DateFromCol = 10; // J
        const int DateToCol   = 11; // K
        const int LabelCol    = 12; // L
        const int FirstMetric = 13; // M

        // Each summary column pulls from the matching data column.
        var metrics = new (string Header, string Col)[]
        {
            ("Temp", "D"),   // Temp (°C)
            ("Hum",  "E"),   // RH (%)
            ("Volt", "F"),   // V1 (V) — the mains channel; V2/V3 are unused on this rig
            ("Freq", "I"),   // Freq (Hz)
        };
        var stats = new (string Label, string Func)[]
        {
            ("Min",     "MIN"),
            ("Max",     "MAX"),
            ("Average", "AVERAGE"),
        };

        // Date-range block: labels on row 1, values on row 2.
        SummaryHeader(ws.Cell(1, DateFromCol), "Date from");
        SummaryHeader(ws.Cell(1, DateToCol),   "Date to");
        SummaryMeta  (ws.Cell(2, DateFromCol), firstDate ?? "");
        SummaryMeta  (ws.Cell(2, DateToCol),   lastDate  ?? "");

        // Metric headers across row 1.
        for (int i = 0; i < metrics.Length; i++)
            SummaryHeader(ws.Cell(1, FirstMetric + i), metrics[i].Header);

        // Stat rows: label in column L, whole-column formula per metric.
        for (int s = 0; s < stats.Length; s++)
        {
            int row = 2 + s;
            SummaryRowLabel(ws.Cell(row, LabelCol), stats[s].Label);
            for (int i = 0; i < metrics.Length; i++)
            {
                var cell = ws.Cell(row, FirstMetric + i);
                cell.FormulaA1 = $"{stats[s].Func}({metrics[i].Col}:{metrics[i].Col})";
                cell.Style.NumberFormat.Format = "0.00";
                cell.Style.Font.FontName = "Consolas";
                cell.Style.Font.FontSize = 10;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
            }
        }

        ws.Column(DateFromCol).Width = 12;
        ws.Column(DateToCol).Width   = 12;
        ws.Column(LabelCol).Width    = 9;
    }

    private static void SummaryHeader(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 11;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderNavy);
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(203, 213, 225);
    }

    private static void SummaryRowLabel(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 11;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(MetaLight);
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
    }

    private static void SummaryMeta(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Font.FontName = "Consolas";
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(MetaLight);
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);
    }

    private readonly record struct ColumnIndex(int Date, int Time, int Temp, int Rh, int V1, int V2, int V3, int Freq);

    private static ColumnIndex MapColumns(string[] header)
    {
        int Find(params string[] names)
        {
            for (int i = 0; i < header.Length; i++)
                foreach (var n in names)
                    if (string.Equals(header[i].Trim(), n, StringComparison.OrdinalIgnoreCase))
                        return i;
            return -1;
        }
        return new ColumnIndex(
            Date: Find("date"),
            Time: Find("time"),
            Temp: Find("temp_c"),
            Rh:   Find("rh_pct"),
            V1:   Find("ch1_v_rms"),
            V2:   Find("ch2_v_rms"),
            V3:   Find("ch3_v_rms"),
            Freq: Find("freq_hz"));
    }

    private static List<string[]> ReadCsv(string path)
    {
        // Open with FileShare.ReadWrite so we can read the CSV even while
        // CsvLogger still has it open for appending.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                     FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        var rows = new List<string[]>();
        string? line;
        while ((line = sr.ReadLine()) is not null)
            rows.Add(line.Split(','));
        return rows;
    }
}
