# Newton4thGui — PPA5500/5530 Remote Console

A WPF GUI for the Newtons4th **PPA5500 / PPA5530** Precision Power Analyzer.

Replicates the instrument's Power Analyzer LCD on screen, polls live readings over RS232 or USB, captures synchronized 3-phase logs to CSV + JSON, and exports formatted XLSX with one click.

![status](https://img.shields.io/badge/status-working-success)
![dotnet](https://img.shields.io/badge/.NET-8.0%20WPF-blue)

## Features

- **Live Power Analyzer display** — 3-phase grid (V, I, W, VA, VAr, pf, V ph-ph, V THD, I THD, freq) updating in real time, formatted in engineering units to match the instrument LCD
- **Real-time CSV logger** with user-configurable interval (minutes / hours), per-row flush — safe for week-long captures
- **Parallel NDJSON file** for structured machine consumption
- **One-click XLSX export** with phase-coloured columns, frozen header + date/time, auto-filter, engineering number formats
- **Live captured-rows table** in the Logger tab so you can see logging happen
- **Auto-detected COM ports** with friendly names (`COM5 — Prolific PL2303GT [RS232 cable]` vs `COM7 — USB Serial [USB]`) — won't accidentally pick the wrong adapter
- **Resilient comms layer** — terminator-timeout resync, field-count guards, inter-query pacing — survives the unit's quirks (see [report](Newton4thGui_REPORT.md))

## Verified working configuration

| | |
|---|---|
| Instrument | Newtons4th **PPA5530** firmware **2.180** |
| Cable | Prolific PL2303GT USB-to-RS232 |
| Port | COM5 |
| Baud / framing | **19200 8 N 1** |
| Flow control | None |
| Terminator | `\r` only (no LF) |

USB-direct (FTDI inside the unit) also works but doesn't speed up the protocol — the unit's internal UART parser is clocked at 19200 max.

## Build & run

```powershell
cd Newton4thGui
dotnet build -c Debug
dotnet run --no-build
```

Requires .NET 8 SDK (Windows). The CSV/JSON/XLSX log files are written to `%USERPROFILE%\Documents\PPA5500_Logs\`.

## Architecture

```
Newton4thGui/
├── App.xaml + .cs           ← display styles (LCD black/yellow look)
├── MainWindow.xaml + .cs    ← top toolbar, two tabs, status bar
├── Services/
│   ├── Ppa5500Client.cs     ← SerialPort wrapper with resync logic
│   ├── CsvLogger.cs         ← CSV + parallel NDJSON, per-row flush
│   ├── XlsxExporter.cs      ← ClosedXML formatted export
│   └── SerialPortInfo.cs    ← WMI-enriched COM enumeration
├── Models/
│   ├── PowerSnapshot.cs     ← one captured cycle
│   └── LogRow.cs            ← log-table ViewModel
└── ViewModels/
    ├── ViewModelBase.cs / RelayCommand.cs / PhaseRowVm.cs
    └── MainViewModel.cs     ← poll loop, logger, all commands
```

## Per-cycle queries

Six SCPI-style ASCII queries per poll cycle (~1.5 s at 19200 baud):

| # | Query | Reply fields | Used for |
|---|---|---|---|
| 1 | `POWER,RMS?` | 13 | freq + V_rms, V_dc, I_rms, I_dc per phase |
| 2 | `POWER,WVA?` | 10 | watts per phase |
| 3 | `POWER,PH-PH?` | 10 | phase-to-phase voltage |
| 4–6 | `HARMON,1?` `,2?` `,3?` | 11 each | V THD %, I THD % per phase |

VA, VAr, pf, dc-watts, dc-% are **derived** in software (match the front panel display).

## CSV / JSON / XLSX format

Each log file is a self-contained per-session capture:

```
PPA5500_log_yyyymmdd_hhmmss.csv     ← spreadsheet view (42 columns)
PPA5500_log_yyyymmdd_hhmmss.jsonl   ← NDJSON, one row per line
PPA5500_log_yyyymmdd_hhmmss.xlsx    ← formatted (created on Export click)
```

Columns: `date, time, freq_hz` + per phase × 3: `v_rms, v_dc, i_rms, i_dc, watts, va, var, pf, watts_dc, dc_percent, v_phph, v_thd_pct, i_thd_pct`.

`date` and `time` are **local computer time** (not UTC).

## Hard-won lessons

The instrument's ASCII protocol has **no message ID** — if a single reply is dropped the whole conversation drifts by one slot, *forever*, silently producing garbage values that look like cable / baud / encoding bugs. See **[Newton4thGui_REPORT.md](Newton4thGui_REPORT.md)** in the project root (one level up from this repo) for a full debug journal of all 12 issues encountered and how they were fixed.

## License

Private project.
