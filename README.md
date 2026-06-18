# Newton4thGui — PPA5xxx Remote Console

A WPF GUI for the Newtons4th **PPA5500 / PPA5530 / PPA530** Precision Power Analyzer family.

Replicates the instrument's Power Analyzer LCD on screen, polls live readings over **RS232, USB-CDC, or LAN**, captures synchronized 3-phase logs with ambient temp / RH from a Rotronic probe to CSV + JSON, and exports a slim formatted XLSX with one click.

![status](https://img.shields.io/badge/status-working-success)
![dotnet](https://img.shields.io/badge/.NET-8.0%20WPF-blue)

## Features

- **Live Power Analyzer display** — 3-phase grid (V, I, W, VA, VAr, pf, V ph-ph, V THD, I THD, freq) updating in real time, formatted in engineering units to match the instrument LCD
- **Three transports** — RS232 cable, the unit's own USB port, or LAN (Lantronix XPort) for units with the LAN option. UI picks one at connect time; the wire protocol is identical
- **Ambient lab strip** — Rotronic HygroClip temp + RH polled on its own slow timer (5 s) and merged into every log row
- **Real-time CSV logger** with user-configurable interval (minutes / hours), per-row flush — safe for week-long captures
- **Parallel NDJSON file** for structured machine consumption
- **One-click slim XLSX export** — Day, Date, Time, Temp (°C), RH (%), V1, V2, V3, Freq (Hz). Calibration paperwork friendly. The raw 42-column CSV stays on disk untouched
- **Live captured-rows table** in the Logger tab so you can see logging happen
- **Auto-detected COM ports** with friendly names (`COM5 — Prolific PL2303GT [RS232 cable]` vs `COM7 — USB Serial [USB]`) — won't accidentally pick the wrong adapter
- **Resilient comms layer** — terminator-timeout resync, field-count guards, inter-query pacing — survives the unit's quirks (see [report](../Newton4thGui_REPORT.md))

## Verified working configurations

### Serial (PPA5500 / PPA5530)

| | |
|---|---|
| Instrument | Newtons4th **PPA5530** firmware **2.180** |
| Cable | Prolific PL2303GT USB-to-RS232 (or the unit's own USB port) |
| Port | COM5 (varies) |
| Baud / framing | **19200 8 N 1** |
| Flow control | None |
| Terminator | `\r` only (no LF) |

### LAN (PPA530 with Lantronix XPort option)

| | |
|---|---|
| Instrument | Newtons4th **PPA530** firmware **2.87** (bench unit "CT 191", s/n 115-01543) |
| Network | 10/100 Base-T, fixed IP set in REMOTE menu |
| Default host / port | `192.168.41.200 : 10001` (Lantronix raw-data tunnel) |
| Terminator | `\r` only (XPort silently ignores LF) |

Port 23 on the unit is also open but it's the telnet/config channel — do not use it for command/reply traffic.

## Build & run

```powershell
cd Newton4thGui
dotnet build -c Debug
dotnet run --no-build
```

Requires .NET 8 SDK (Windows). Log files are written to `%USERPROFILE%\Documents\PPA5500_Logs\`.

## Architecture

```
Newton4thGui/
├── App.xaml + .cs              ← display styles (LCD black/yellow look)
├── MainWindow.xaml + .cs       ← top toolbar (Serial row + LAN row), two tabs, status bar
├── Services/
│   ├── IPpaClient.cs           ← transport-neutral interface (Query/Send/Resync/Close)
│   ├── Ppa5500Client.cs        ← RS232/USB-CDC implementation (SerialPort)
│   ├── Ppa5500LanClient.cs     ← LAN/TCP implementation (NetworkStream)
│   ├── RotronicClient.cs       ← USB-HID temp/RH probe (HygroClip family)
│   ├── CsvLogger.cs            ← CSV + parallel NDJSON, per-row flush
│   ├── XlsxExporter.cs         ← ClosedXML slim export (Day/Date/Time/T/RH/V1-3/Freq + Min/Max/Avg summary)
│   └── SerialPortInfo.cs       ← WMI-enriched COM enumeration
├── Models/
│   ├── PowerSnapshot.cs        ← one captured cycle
│   ├── EnvironmentSample.cs    ← one Rotronic reading
│   └── LogRow.cs               ← log-table ViewModel
└── ViewModels/
    ├── ViewModelBase.cs / RelayCommand.cs / PhaseRowVm.cs
    └── MainViewModel.cs        ← transport switch, poll loop, env loop, logger, commands
```

## Per-cycle queries

Six SCPI-style ASCII queries per poll cycle:

| # | Query | Reply fields | Used for |
|---|---|---|---|
| 1 | `POWER,RMS?` | 13 | freq + V_rms, V_dc, I_rms, I_dc per phase |
| 2 | `POWER,WVA?` | 10 | watts per phase |
| 3 | `POWER,PH-PH?` | 10 | phase-to-phase voltage |
| 4–6 | `HARMON,1?` `,2?` `,3?` | 11 each | V THD %, I THD % per phase |

VA, VAr, pf, dc-watts, dc-% are **derived** in software (match the front panel display).

**Transport latency** — LAN adds ~50–100 ms per round-trip vs serial because the Lantronix XPort buffers each frame between the TCP socket and the unit's internal UART. Six round-trips per cycle ≈ 200–500 ms on LAN vs 50–150 ms on serial. If a faster LAN cadence is needed, batch with semicolon syntax (`POWER,RMS?;POWER,WVA?;POWER,PH-PH?` in one send).

## CSV / JSON / XLSX format

Each log file is a self-contained per-session capture:

```
PPA5500_log_yyyymmdd_hhmmss.csv     ← raw spreadsheet view (date, time, temp_c, rh_pct, freq_hz, + 13 cols × 3 phases = 42)
PPA5500_log_yyyymmdd_hhmmss.jsonl   ← NDJSON, one row per line
PPA5500_log_yyyymmdd_hhmmss.xlsx    ← slim formatted export (created on Export click)
```

CSV columns: `date, time, temp_c, rh_pct, freq_hz` + per phase × 3: `v_rms, v_dc, i_rms, i_dc, watts, va, var, pf, watts_dc, dc_percent, v_phph, v_thd_pct, i_thd_pct`.

XLSX columns (slim, for paperwork): **Day, Date, Time, Temp (°C), RH (%), V1 (V), V2 (V), V3 (V), Freq (Hz)**.

`date` and `time` are **local computer time** (not UTC).

## Hard-won lessons

The instrument's ASCII protocol has **no message ID** — if a single reply is dropped the whole conversation drifts by one slot, *forever*, silently producing garbage values that look like cable / baud / encoding bugs. The Ctrl-T device-clear is the resync escape, and we apply it on every dropped terminator. See **[Newton4thGui_REPORT.md](../Newton4thGui_REPORT.md)** in the project root for the full debug journal.

## License

Private project.
