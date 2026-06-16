using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace OrokinMonitor
{
    // Visitor required by the library to refresh every hardware node + subnode.
    internal sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    // One flat snapshot of everything the UI shows. Nulls mean "sensor not found".
    public sealed class Snapshot
    {
        public string CpuName = "CPU";
        public float? CpuTemp;        // °C
        public float? CpuClock;       // MHz (max core)
        public float? CpuLoad;        // %
        public float? CpuPower;       // W (package)

        public string GpuName = "GPU";
        public float? GpuTemp;        // °C
        public float? GpuLoad;        // %
        public float? GpuClock;       // MHz (core)
        public float? GpuVoltage;     // V  (NVIDIA usually does not report this)
        public float? GpuFan;         // RPM or % depending on card
        public bool   GpuFanIsPercent;
        public float? GpuHotspot;     // °C  (hot spot / memory junction)
        public float? GpuPower;       // W
        public float? GpuVramUsed;    // MB

        public float? RamLoad;        // %
        public float? RamUsedGb;      // GB
        public float? RamTemp;        // °C (only if a DIMM/board sensor exists)

        public float? TotalPower;     // W  = CpuPower + GpuPower (see caveat in README)

        // Idle/load envelope for total power, tracked across the session.
        public float? TotalPowerMin;
        public float? TotalPowerMax;
    }

    public sealed class SensorService : IDisposable
    {
        private readonly Computer _computer;
        private readonly UpdateVisitor _visitor = new();

        private float? _totalMin;
        private float? _totalMax;

        public SensorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,   // needed for some RAM/board temps
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false,
                // Note: IsPowerMonitorEnabled was added in a later LHM version.
                // CPU package power and GPU power come from their own hardware
                // nodes regardless, so it is not needed here.
            };
            _computer.Open();
        }

        public Snapshot Read()
        {
            _computer.Accept(_visitor);
            var s = new Snapshot();

            IHardware? cpu = First(HardwareType.Cpu);
            IHardware? gpu = FirstGpu();
            IHardware? mem = First(HardwareType.Memory);

            if (cpu != null)
            {
                s.CpuName = Pretty(cpu.Name);
                // Prefer a "Package"/"Tctl" style temp; fall back to max core temp.
                s.CpuTemp = Pick(cpu, SensorType.Temperature,
                                 prefer: new[] { "package", "tctl", "tdie", "cpu" })
                            ?? Max(cpu, SensorType.Temperature);
                s.CpuClock = Max(cpu, SensorType.Clock);   // highest active core clock
                s.CpuLoad = Pick(cpu, SensorType.Load, prefer: new[] { "total" })
                            ?? Max(cpu, SensorType.Load);
                s.CpuPower = Pick(cpu, SensorType.Power, prefer: new[] { "package" })
                            ?? Max(cpu, SensorType.Power);
            }

            if (gpu != null)
            {
                s.GpuName = Pretty(gpu.Name);
                s.GpuTemp = Pick(gpu, SensorType.Temperature, prefer: new[] { "core", "gpu" })
                            ?? Max(gpu, SensorType.Temperature);
                s.GpuLoad = Pick(gpu, SensorType.Load, prefer: new[] { "core", "gpu" })
                            ?? Max(gpu, SensorType.Load);
                s.GpuClock = Pick(gpu, SensorType.Clock, prefer: new[] { "core" })
                            ?? Max(gpu, SensorType.Clock);
                s.GpuVoltage = Pick(gpu, SensorType.Voltage, prefer: new[] { "core", "gpu" });

                // Fan: prefer RPM (SensorType.Fan); fall back to % (SensorType.Control).
                s.GpuFan = First(gpu, SensorType.Fan);
                if (s.GpuFan.HasValue) s.GpuFanIsPercent = false;
                else
                {
                    s.GpuFan = Pick(gpu, SensorType.Control, prefer: new[] { "fan", "gpu" })
                               ?? First(gpu, SensorType.Control);
                    s.GpuFanIsPercent = s.GpuFan.HasValue;
                }

                // Hotspot / memory-junction: a Temperature sensor that isn't the
                // main core reading. Match common names, else take the hottest
                // non-core temp.
                s.GpuHotspot = Pick(gpu, SensorType.Temperature,
                                    prefer: new[] { "hot spot", "hotspot", "junction", "memory" });

                s.GpuPower = Pick(gpu, SensorType.Power, prefer: new[] { "total", "package", "gpu" })
                            ?? Max(gpu, SensorType.Power);
                // VRAM used: SmallData in MB on most LHM builds.
                s.GpuVramUsed = Pick(gpu, SensorType.SmallData, prefer: new[] { "used" });
            }

            if (mem != null)
            {
                s.RamLoad = Pick(mem, SensorType.Load, prefer: new[] { "memory" })
                            ?? First(mem, SensorType.Load);
                s.RamUsedGb = Pick(mem, SensorType.Data, prefer: new[] { "used" });
                s.RamTemp = First(mem, SensorType.Temperature);
            }

            // Total = CPU package + GPU power. Either may be null.
            if (s.CpuPower.HasValue || s.GpuPower.HasValue)
            {
                float total = (s.CpuPower ?? 0) + (s.GpuPower ?? 0);
                s.TotalPower = total;

                _totalMin = _totalMin.HasValue ? Math.Min(_totalMin.Value, total) : total;
                _totalMax = _totalMax.HasValue ? Math.Max(_totalMax.Value, total) : total;
                s.TotalPowerMin = _totalMin;
                s.TotalPowerMax = _totalMax;
            }

            return s;
        }

        public void ResetPowerEnvelope()
        {
            _totalMin = null;
            _totalMax = null;
        }

        // ---- helpers ----

        private IHardware? First(HardwareType type) =>
            _computer.Hardware.FirstOrDefault(h => h.HardwareType == type);

        private IHardware? FirstGpu()
        {
            // A Ryzen 7600 reports an iGPU that can enumerate before the real
            // card, so don't just take the first GPU. Order of preference:
            //   1) NVIDIA discrete  2) any GPU whose name isn't obviously an iGPU
            //   3) whatever GPU exists (last resort).
            var gpus = _computer.Hardware.Where(h =>
                h.HardwareType == HardwareType.GpuNvidia ||
                h.HardwareType == HardwareType.GpuAmd ||
                h.HardwareType == HardwareType.GpuIntel).ToList();

            if (gpus.Count == 0) return null;
            if (gpus.Count == 1) return gpus[0];

            // Prefer NVIDIA (your 3080) when present.
            var nv = gpus.FirstOrDefault(g => g.HardwareType == HardwareType.GpuNvidia);
            if (nv != null) return nv;

            // Otherwise skip anything that looks integrated.
            var discrete = gpus.FirstOrDefault(g =>
                !g.Name.Contains("Graphics", StringComparison.OrdinalIgnoreCase) &&
                !g.Name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) &&
                !g.Name.Contains("iGPU", StringComparison.OrdinalIgnoreCase));
            return discrete ?? gpus[0];
        }

        // Pick first sensor of a type whose name contains any preferred keyword.
        private static float? Pick(IHardware hw, SensorType type, string[] prefer)
        {
            foreach (string key in prefer)
            {
                var hit = hw.Sensors.FirstOrDefault(x =>
                    x.SensorType == type &&
                    x.Name.Contains(key, StringComparison.OrdinalIgnoreCase) &&
                    x.Value.HasValue);
                if (hit?.Value is float v) return v;
            }
            return null;
        }

        private static float? First(IHardware hw, SensorType type) =>
            hw.Sensors.FirstOrDefault(x => x.SensorType == type && x.Value.HasValue)?.Value;

        private static float? Max(IHardware hw, SensorType type)
        {
            var vals = hw.Sensors
                .Where(x => x.SensorType == type && x.Value.HasValue)
                .Select(x => x.Value!.Value).ToList();
            return vals.Count > 0 ? vals.Max() : (float?)null;
        }

        private static string Pretty(string raw) => raw.Trim();

        public void Dispose() => _computer.Close();
    }
}
