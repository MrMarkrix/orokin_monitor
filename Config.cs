using System;
using System.IO;
using System.Text.Json;

namespace OrokinMonitor
{
    public sealed class Config
    {
        public string Theme { get; set; } = "orokin";
        public double Latitude { get; set; } = 50.0755;   // Prague default
        public double Longitude { get; set; } = 14.4378;
        public string LocationName { get; set; } = "Prague";
        public bool UseFahrenheit { get; set; } = false;
        public int CpuTempWarn { get; set; } = 85;          // °C, turns readout "hot" color
        public int GpuTempWarn { get; set; } = 80;
        public int RefreshMs { get; set; } = 1000;

        private static string Path =>
            System.IO.Path.Combine(AppContext.BaseDirectory, "orokin-config.json");

        public static Config Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    var json = File.ReadAllText(Path);
                    var c = JsonSerializer.Deserialize<Config>(json);
                    if (c != null) return c;
                }
            }
            catch { /* fall through to defaults */ }
            return new Config();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path, json);
            }
            catch { /* non-fatal */ }
        }
    }
}
