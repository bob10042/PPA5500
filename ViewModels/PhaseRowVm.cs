using System;
using System.Globalization;

namespace Newton4thGui.ViewModels;

public sealed class PhaseRowVm : ViewModelBase
{
    private double _watts, _va, _vAr, _pf;
    private double _vRms, _iRms, _vDc, _iDc;
    private double _wDc, _dcPct, _vPhPh;
    private double _vThd, _iThd;

    public int Phase { get; init; }

    // Setters (raw values)
    public double Watts  { get => _watts; set => Set(ref _watts, value); }
    public double VA     { get => _va;    set => Set(ref _va,    value); }
    public double VAr    { get => _vAr;   set => Set(ref _vAr,   value); }
    public double Pf     { get => _pf;    set => Set(ref _pf,    value); }
    public double VRms   { get => _vRms;  set => Set(ref _vRms,  value); }
    public double IRms   { get => _iRms;  set => Set(ref _iRms,  value); }
    public double VDc    { get => _vDc;   set => Set(ref _vDc,   value); }
    public double IDc    { get => _iDc;   set => Set(ref _iDc,   value); }
    public double WattsDc{ get => _wDc;   set => Set(ref _wDc,   value); }
    public double DcPct  { get => _dcPct; set => Set(ref _dcPct, value); }
    public double VPhPh  { get => _vPhPh; set => Set(ref _vPhPh, value); }
    public double VThd   { get => _vThd;  set => Set(ref _vThd,  value); }
    public double IThd   { get => _iThd;  set => Set(ref _iThd,  value); }

    // Display strings (auto-engineering format)
    public string WattsText  => Eng(_watts);
    public string VAText     => Eng(_va);
    public string VArText    => Eng(_vAr);
    public string PfText     => _pf.ToString("0.0000", CultureInfo.InvariantCulture);
    public string VRmsText   => Eng(_vRms);
    public string IRmsText   => Eng(_iRms);
    public string VDcText    => Eng(_vDc);
    public string IDcText    => Eng(_iDc);
    public string WattsDcText=> Eng(_wDc);
    public string DcPctText  => _dcPct.ToString("0.000", CultureInfo.InvariantCulture);
    public string VPhPhText  => Eng(_vPhPh);
    public string VThdText   => double.IsNaN(_vThd) ? "-" : Eng(_vThd) + " %";
    public string IThdText   => double.IsNaN(_iThd) ? "-" : Eng(_iThd) + " %";

    public PhaseRowVm()
    {
        PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(Watts):    Raise(nameof(WattsText));    break;
                case nameof(VA):       Raise(nameof(VAText));       break;
                case nameof(VAr):      Raise(nameof(VArText));      break;
                case nameof(Pf):       Raise(nameof(PfText));       break;
                case nameof(VRms):     Raise(nameof(VRmsText));     break;
                case nameof(IRms):     Raise(nameof(IRmsText));     break;
                case nameof(VDc):      Raise(nameof(VDcText));      break;
                case nameof(IDc):      Raise(nameof(IDcText));      break;
                case nameof(WattsDc):  Raise(nameof(WattsDcText));  break;
                case nameof(DcPct):    Raise(nameof(DcPctText));    break;
                case nameof(VPhPh):    Raise(nameof(VPhPhText));    break;
                case nameof(VThd):     Raise(nameof(VThdText));     break;
                case nameof(IThd):     Raise(nameof(IThdText));     break;
            }
        };
    }

    /// <summary>Engineering-style format with 5-digit mantissa (matches PPA5500 LCD default).</summary>
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

        // 5 significant figures: 999.99 / 99.999 / 9.9999
        string fmt = scaled >= 100 ? "0.00"
                   : scaled >= 10  ? "0.000"
                   :                 "0.0000";
        return sign + scaled.ToString(fmt, ci) + suffix;
    }
}
