namespace GhostDeck;

/// <summary>Uses only the same device-profile tables and mode path as the existing curve editor.</summary>
public sealed class EcCrossCoolingHardware : ICrossCoolingHardware
{
    private readonly DeviceProfile _device;
    private readonly FanCurveSpec _spec;
    private readonly byte _automatic;
    public byte AdvancedMode => _spec.AdvancedModeValue;
    public EcCrossCoolingHardware(DeviceProfile device)
    {
        _device = device;
        _spec = device.FanCurve ?? throw new ArgumentException("No fan tables");
        if (_spec.SingleFan || _spec.Points != 6 || Ec.OnBackupPath) throw new ArgumentException("No dual fan method interface");
        _automatic = device.Recipes[ProfileId.Balanced].First(p => p.Item1 == device.FanMode).Item2;
    }

    public CoolingBaseline Capture()
    {
        var curve = Ec.ReadFanCurve(_device) ?? throw new InvalidOperationException("Fan tables could not be read");
        return new(new FanCurvePreset { CpuTemp = curve.cpuTemp, CpuSpeed = curve.cpuSpeed,
            GpuTemp = curve.gpuTemp, GpuSpeed = curve.gpuSpeed }, Ec.ReadByte(_device.FanMode));
    }

    public CoolingReading Read()
    {
        var started = DateTimeOffset.UtcNow;
        if (!Ec.TryReadHw(_device, out var hw)) throw new InvalidOperationException("Fan telemetry could not be read");
        return new(started, hw, Ec.ReadByte(_device.FanMode), Ec.GetCoolerBoost(_device));
    }

    public void Apply(FanCurvePreset original, int[] cpuSpeeds, int[] gpuSpeeds)
    {
        Ec.WriteFanCurve(_device, original.CpuTemp, cpuSpeeds, original.GpuTemp, gpuSpeeds);
        if (Ec.ReadByte(_device.FanMode) != AdvancedMode) Ec.SetFanMode(_device, AdvancedMode);
    }

    public void Restore(CoolingBaseline baseline)
    {
        var c = baseline.Curve;
        Ec.WriteFanCurve(_device, c.CpuTemp, c.CpuSpeed, c.GpuTemp, c.GpuSpeed);
        Ec.SetFanMode(_device, baseline.Mode);
    }

    public void Automatic() => Ec.SetFanMode(_device, _automatic);
}
