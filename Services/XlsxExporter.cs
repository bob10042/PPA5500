using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;

namespace Newton4thGui.Services;

/// <summary>
/// Exports a PPA5500 CSV log to a formatted XLSX with phase-coloured columns,
/// engineering number formats, frozen header + date/time columns, and an
/// auto-filter on the data range. Mirrors the styling of the calibration
/// software's Excel exports.
/// </summary>
public static class XlsxExporter
{
    private const string ColMeta   = "FF0F172A";  // dark navy   for date / time / freq
    private const string ColCh1    = "FF2563EB";  // deep blue   for ch1 header
    private const string ColCh2    = "FF059669";  // green       for ch2 header
    private const string ColCh3    = "FFD97706";  // orange      for ch3 header
    private const string Ch1Light  = "FFDBEAFE";  // light blue
    private const string Ch2Light  = "FFD1FAE5";  // light green
    private const string Ch3Light  = "FFFED7AA";  // light orange
    private const string TitleNavy = "FF0F172A";

    /// <summary>Convert a CSV file to XLSX. Returns the output path.</summary>
    public static string Convert(string csvPath, string? outputPath = null)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("CSV not found", csvPath);

        outputPath ??= Path.ChangeExtension(csvPath, ".xlsx");

        var rows = ReadCsv(csvPath);
        if (rows.Count == 0) throw new InvalidDataException("CSV is empty");
        var header = rows[0];
        var data = rows.GetRange(1, rows.Count - 1);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("PPA5500 Log");

        // Title row
        var title = ws.Cell(1, 1);
        title.Value = "PPA5530  Power Analyzer Log";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        title.Style.Font.FontColor = XLColor.FromArgb(15, 23, 42);

        var src = ws.Cell(1, 4);
        src.Value = "Source: " + Path.GetFileName(csvPath);
        src.Style.Font.Italic = true;
        src.Style.Font.FontSize = 10;
        src.Style.Font.FontColor = XLColor.FromArgb(100, 116, 139);

        ws.Row(1).Height = 22;

        // Header row at row 3
        for (int i = 0; i < header.Length; i++)
        {
            var name = header[i];
            var cell = ws.Cell(3, i + 1);
            cell.Value = name;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 11;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(203, 213, 225);
            cell.Style.Fill.BackgroundColor = name.StartsWith("ch1_") ? XLColor.FromHtml(ColCh1)
                                            : name.StartsWith("ch2_") ? XLColor.FromHtml(ColCh2)
                                            : name.StartsWith("ch3_") ? XLColor.FromHtml(ColCh3)
                                            :                            XLColor.FromHtml(ColMeta);
        }
        ws.Row(3).Height = 30;

        // Data rows
        for (int r = 0; r < data.Count; r++)
        {
            var rowVals = data[r];
            int xlRow = 4 + r;
            for (int i = 0; i < header.Length && i < rowVals.Length; i++)
            {
                var name = header[i];
                var cell = ws.Cell(xlRow, i + 1);
                var val  = rowVals[i];

                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromArgb(226, 232, 240);

                if (name == "date" || name == "time" || name == "timestamp_iso")
                {
                    cell.Value = val;
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.FromHtml("FFF8FAFC");
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(ColMeta);
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Font.FontName = "Consolas";
                }
                else
                {
                    if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                    {
                        cell.Value = fv;
                        // 2 decimal places for every measurement column.
                        cell.Style.NumberFormat.Format = "0.00";
                    }
                    else
                    {
                        cell.Value = val;
                    }
                    cell.Style.Font.FontName = "Consolas";
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    cell.Style.Fill.BackgroundColor = name.StartsWith("ch1_") ? XLColor.FromHtml(Ch1Light)
                                                    : name.StartsWith("ch2_") ? XLColor.FromHtml(Ch2Light)
                                                    : name.StartsWith("ch3_") ? XLColor.FromHtml(Ch3Light)
                                                    :                            XLColor.NoColor;
                }
            }
        }

        // Column widths
        for (int i = 0; i < header.Length; i++)
        {
            var name = header[i];
            int w = name == "date"            ? 12
                  : name == "time"            ? 14
                  : name == "timestamp_iso"   ? 30
                  : name.Contains("thd")      ? 12
                  :                              14;
            ws.Column(i + 1).Width = w;
        }

        // Freeze panes — keep header row + date/time visible while scrolling
        bool oldSchema = header.Length > 0 && header[0] == "timestamp_iso";
        ws.SheetView.FreezeRows(3);
        ws.SheetView.FreezeColumns(oldSchema ? 1 : 2);

        // Auto-filter on the entire data range
        if (data.Count > 0)
        {
            var range = ws.Range(3, 1, 3 + data.Count, header.Length);
            range.SetAutoFilter();
        }

        // Save
        wb.SaveAs(outputPath);
        return outputPath;
    }

    private static List<string[]> ReadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        var rows = new List<string[]>(lines.Length);
        foreach (var line in lines)
        {
            // Simple CSV — our writer never emits quoted fields with commas,
            // so a plain split is sufficient.
            rows.Add(line.Split(','));
        }
        return rows;
    }
}
