using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Newton4thGui.Models;
using Newton4thGui.Services;

namespace Newton4thGui.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly Ppa5500Client _client = new();
    private readonly CsvLogger _csv = new();
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private DateTime _lastLogUtc = DateTime.MinValue;

    // ---- connection ----
    private string _portName = "COM5";
    public string PortName { get => _portName; set => Set(ref _portName, value); }

    private int _baudRate = 19200;
    public int BaudRate { get => _baudRate; set => Set(ref _baudRate, value); }

    public ObservableCollection<SerialPortInfo> AvailablePorts { get; } = new();

    private SerialPortInfo? _selectedPort;
    public SerialPortInfo? SelectedPort
    {
        get => _selectedPort;
        set { if (Set(ref _selectedPort, value) && value is not null) PortName = value.PortName; }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (Set(ref _isConnected, value))
            {
                Raise(nameof(NotConnected));
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
                StartLoggingCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool NotConnected => !_isConnected;

    private string _identity = "";
    public string Identity { get => _identity; set => Set(ref _identity, value); }

    private string _status = "Disconnected.";
    public string Status { get => _status; set => Set(ref _status, value); }

    // ---- live readings ----
    public PhaseRowVm Phase1 { get; } = new() { Phase = 1 };
    public PhaseRowVm Phase2 { get; } = new() { Phase = 2 };
    public PhaseRowVm Phase3 { get; } = new() { Phase = 3 };

    private double _frequency;
    public double Frequency { get => _frequency; set { if (Set(ref _frequency, value)) Raise(nameof(FrequencyText)); } }
    public string FrequencyText => double.IsNaN(_frequency) ? "-" : _frequency.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    private double _refreshHz = 2.0;
    public double RefreshHz { get => _refreshHz; set => Set(ref _refreshHz, value); }

    // ---- logger ----
    private string _logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PPA5500_Logs");
    public string LogFolder { get => _logFolder; set => Set(ref _logFolder, value); }

    private double _logIntervalValue = 1.0;
    public double LogIntervalValue { get => _logIntervalValue; set => Set(ref _logIntervalValue, value); }

    /// <summary>Unit: "Minutes" or "Hours".</summary>
    private string _logIntervalUnit = "Minutes";
    public string LogIntervalUnit { get => _logIntervalUnit; set => Set(ref _logIntervalUnit, value); }

    private bool _isLogging;
    public bool IsLogging { get => _isLogging; private set { if (Set(ref _isLogging, value)) { StartLoggingCommand.RaiseCanExecuteChanged(); StopLoggingCommand.RaiseCanExecuteChanged(); } } }

    private string _logStatus = "Idle.";
    public string LogStatus { get => _logStatus; set => Set(ref _logStatus, value); }

    private long _rowsWritten;
    public long RowsWritten { get => _rowsWritten; set => Set(ref _rowsWritten, value); }

    private string? _logFile;
    public string? LogFile { get => _logFile; set => Set(ref _logFile, value); }

    private string? _jsonFile;
    public string? JsonFile { get => _jsonFile; set => Set(ref _jsonFile, value); }

    /// <summary>Live tail of rows committed to CSV — bound to the DataGrid in the Logger tab.</summary>
    public ObservableCollection<Models.LogRow> LoggedRows { get; } = new();
    private const int MaxRowsInMemory = 1000;

    // ---- commands ----
    public RelayCommand RefreshPortsCommand { get; }
    public RelayCommand ConnectCommand    { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand StartLoggingCommand { get; }
    public RelayCommand StopLoggingCommand  { get; }
    public RelayCommand ExportXlsxCommand   { get; }

    public MainViewModel()
    {
        RefreshPortsCommand   = new RelayCommand(RefreshPorts);
        ConnectCommand        = new RelayCommand(Connect, () => !IsConnected);
        DisconnectCommand     = new RelayCommand(Disconnect, () => IsConnected);
        StartLoggingCommand   = new RelayCommand(StartLogging, () => IsConnected && !IsLogging);
        StopLoggingCommand    = new RelayCommand(StopLogging,  () => IsLogging);
        ExportXlsxCommand     = new RelayCommand(ExportXlsx);

        RefreshPorts();
    }

    public void RefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var info in SerialPortInfo.Enumerate())
            AvailablePorts.Add(info);

        // Try to keep current selection; otherwise prefer a USB-instrument port,
        // else a Newtons4th-branded port, else first available.
        var match = AvailablePorts.FirstOrDefault(p => p.PortName == PortName)
                 ?? AvailablePorts.FirstOrDefault(p => p.Kind == InterfaceKind.UsbInstrument)
                 ?? AvailablePorts.FirstOrDefault();
        SelectedPort = match;
    }

    public void Connect()
    {
        try
        {
            Status = $"Opening {PortName} @ {BaudRate}...";
            _client.Open(PortName, BaudRate);
            Identity = _client.Identity ?? "";
            IsConnected = true;
            Status = "Connected.";
            StartPolling();
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
            MessageBox.Show(ex.Message, "Connect failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Disconnect()
    {
        StopLogging();
        StopPolling();
        _client.Close();
        IsConnected = false;
        Identity = "";
        Status = "Disconnected.";
    }

    private void StartPolling()
    {
        StopPolling();
        _cts = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoop(_cts.Token));
    }

    private void StopPolling()
    {
        try { _cts?.Cancel(); _pollTask?.Wait(1500); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _pollTask = null;
    }

    private (double v, double i, double vph, double iph)[] _lastThd =
        new (double, double, double, double)[3]
        {
            (double.NaN, double.NaN, double.NaN, double.NaN),
            (double.NaN, double.NaN, double.NaN, double.NaN),
            (double.NaN, double.NaN, double.NaN, double.NaN),
        };
    private int _cycle;

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var t0 = Environment.TickCount;
            try
            {
                var rmsReply  = await _client.QueryAsync("POWER,RMS?",   1500, ct).ConfigureAwait(false);
                var wvaReply  = await _client.QueryAsync("POWER,WVA?",   1500, ct).ConfigureAwait(false);
                var phphReply = await _client.QueryAsync("POWER,PH-PH?", 1500, ct).ConfigureAwait(false);
                var rms  = Ppa5500Client.ParseFloats(rmsReply);
                var wva  = Ppa5500Client.ParseFloats(wvaReply);
                var phph = Ppa5500Client.ParseFloats(phphReply);

                // Field-count guards. If any of the three POWER replies is short
                // (the unit dropped a query, or replies drifted by one), the values
                // we'd read for ph2/ph3 would be from a different reply. Resync
                // and skip this cycle — far better than displaying garbage.
                if (rms.Length < 13 || wva.Length < 10 || phph.Length < 10)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                        Status = $"Resync (rms={rms.Length}, wva={wva.Length}, phph={phph.Length})");
                    _client.Resync();
                    await Task.Delay(150, ct).ConfigureAwait(false);
                    continue;
                }

                // Harmonics: poll all 3 phases every cycle. THD now lives on the
                // main Power Analyzer display so we always need fresh values.
                _cycle++;
                for (int ph = 0; ph < 3; ph++)
                {
                    bool ok = false;
                    for (int attempt = 0; attempt < 2 && !ok; attempt++)
                    {
                        try
                        {
                            var hReply = await _client.QueryAsync($"HARMON,{ph + 1}?", 1500, ct).ConfigureAwait(false);
                            var h = Ppa5500Client.ParseFloats(hReply);
                            if (h.Length >= 11)
                            {
                                _lastThd[ph] = (h[7], h[8], h[9], h[10]);
                                ok = true;
                            }
                            else
                            {
                                // Short reply — typical on HARMON,1? right after the
                                // POWER queries. Resync and retry once so phase 1's
                                // THD doesn't end up NaN in the CSV / display.
                                _client.Resync();
                                await Task.Delay(120, ct).ConfigureAwait(false);
                            }
                        }
                        catch { _client.Resync(); await Task.Delay(120, ct).ConfigureAwait(false); }
                    }
                }

                var snap = BuildSnapshot(rms, wva, phph, _lastThd, rmsReply, wvaReply, phphReply);
                Application.Current?.Dispatcher.Invoke(() => Apply(snap));
                MaybeLog(snap);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher.Invoke(() => Status = "Poll error: " + ex.Message);
                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            var period = (int)Math.Max(50.0, 1000.0 / Math.Max(0.1, RefreshHz));
            var spent = Environment.TickCount - t0;
            var sleep = period - spent;
            if (sleep > 0) await Task.Delay(sleep, ct).ConfigureAwait(false);
        }
    }

    private static PowerSnapshot BuildSnapshot(
        double[] rms, double[] wva, double[] phph,
        (double v, double i, double vph, double iph)[] thd,
        string rmsRaw, string wvaRaw, string phphRaw)
    {
        // Layouts (verified against PPA5530 firmware 2.180):
        //   POWER,RMS?    : freq, [Vrms,Vdc,Irms,Idc] x 3
        //   POWER,WVA?    : freq, [W, Vrms, Irms]     x 3
        //   POWER,PH-PH?  : freq, [Vrms_pp, Vfund_pp, phase_pp] x 3
        double G(double[] a, int i) => i < a.Length ? a[i] : double.NaN;
        var phases = new PhaseReading[3];
        for (int ch = 0; ch < 3; ch++)
        {
            int rmsBase  = 1 + ch * 4;
            int wvaBase  = 1 + ch * 3;
            int phphBase = 1 + ch * 3;
            phases[ch] = new PhaseReading
            {
                Phase = ch + 1,
                VRms  = G(rms,  rmsBase + 0),
                VDc   = G(rms,  rmsBase + 1),
                IRms  = G(rms,  rmsBase + 2),
                IDc   = G(rms,  rmsBase + 3),
                Watts = G(wva,  wvaBase + 0),
                VPhPh = G(phph, phphBase + 0),
                VThdPercent = thd[ch].v,
                IThdPercent = thd[ch].i,
                VPhase = thd[ch].vph,
                IPhase = thd[ch].iph,
            };
        }
        return new PowerSnapshot
        {
            Frequency = G(rms, 0),
            Phases = phases,
            RawRms = rmsRaw,
            RawWva = wvaRaw,
            RawPhPh = phphRaw,
        };
    }

    private void Apply(PowerSnapshot snap)
    {
        Frequency = snap.Frequency;
        ApplyRow(Phase1, snap.Phases[0]);
        ApplyRow(Phase2, snap.Phases[1]);
        ApplyRow(Phase3, snap.Phases[2]);
        Status = $"OK — {DateTime.Now:HH:mm:ss}";
    }

    private static void ApplyRow(PhaseRowVm vm, PhaseReading r)
    {
        vm.VRms = r.VRms; vm.VDc = r.VDc;
        vm.IRms = r.IRms; vm.IDc = r.IDc;
        vm.Watts = r.Watts;
        vm.VA = r.VA; vm.VAr = r.VAr; vm.Pf = r.Pf;
        vm.WattsDc = r.WattsDc; vm.DcPct = r.DcPercent;
        vm.VPhPh = r.VPhPh;
        vm.VThd = r.VThdPercent; vm.IThd = r.IThdPercent;
    }

    // ---- CSV logging ----
    public void StartLogging()
    {
        if (!IsConnected) return;
        try
        {
            _csv.Start(LogFolder);
            LogFile = _csv.FilePath;
            JsonFile = _csv.JsonPath;
            _lastLogUtc = DateTime.MinValue;
            LoggedRows.Clear();
            IsLogging = true;
            LogStatus = "Logging started.";
        }
        catch (Exception ex)
        {
            LogStatus = "Failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Log start failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void StopLogging()
    {
        if (!IsLogging) return;
        _csv.Stop();
        IsLogging = false;
        LogStatus = $"Stopped. {RowsWritten} rows written.";
    }

    private TimeSpan IntervalSpan()
    {
        var v = Math.Max(0.001, LogIntervalValue);
        return LogIntervalUnit.Equals("Hours", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(v)
            : TimeSpan.FromMinutes(v);
    }

    private void MaybeLog(PowerSnapshot snap)
    {
        if (!IsLogging) return;
        var interval = IntervalSpan();
        if (snap.TimestampUtc - _lastLogUtc < interval) return;
        _lastLogUtc = snap.TimestampUtc;
        try
        {
            _csv.Write(snap);
            var row = Models.LogRow.FromSnapshot(snap);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                RowsWritten = _csv.RowsWritten;
                LogStatus = $"Logged {RowsWritten} rows  →  {Path.GetFileName(_csv.FilePath)}";
                LoggedRows.Add(row);
                while (LoggedRows.Count > MaxRowsInMemory) LoggedRows.RemoveAt(0);
            });
        }
        catch (Exception ex)
        {
            Application.Current?.Dispatcher.Invoke(() => LogStatus = "Write error: " + ex.Message);
        }
    }

    public void ExportXlsx()
    {
        // Prefer the currently-open log file. If none, fall back to the most
        // recently modified CSV in the log folder so the button still works
        // after Stop logging.
        var path = _csv.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            try
            {
                if (Directory.Exists(LogFolder))
                {
                    path = Directory.EnumerateFiles(LogFolder, "PPA5500_log_*.csv")
                                    .OrderByDescending(File.GetLastWriteTimeUtc)
                                    .FirstOrDefault();
                }
            }
            catch { /* ignore */ }
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show($"No CSV file found in:\n{LogFolder}\n\nStart logging first.",
                            "Export to XLSX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var outPath = XlsxExporter.Convert(path);
            LogStatus = $"Exported  →  {Path.GetFileName(outPath)}";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = outPath,
                    UseShellExecute = true,
                });
            }
            catch { /* user can open manually */ }
        }
        catch (Exception ex)
        {
            LogStatus = "Export failed: " + ex.Message;
            MessageBox.Show(ex.Message + "\n\n" + ex.StackTrace,
                            "Export to XLSX failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        StopLogging();
        StopPolling();
        _client.Dispose();
        _csv.Dispose();
    }
}
