using System;
using System.Windows;
using LibreHardwareMonitor.Hardware;

namespace OrokinMonitor
{
    public partial class App : Application
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Diagnostic mode: list every sensor your hardware exposes, then exit.
            // Run:  OrokinMonitor.exe --dump > sensors.txt
            foreach (var a in args)
            {
                if (a.Equals("--dump", StringComparison.OrdinalIgnoreCase))
                {
                    DumpSensors();
                    return;
                }
            }

            var app = new App();
            app.InitializeComponent();
            app.Run(new MainWindow());
        }

        private static void DumpSensors()
        {
            // Needs a console; --dump is meant to be run from a terminal.
            AllocConsoleIfNeeded();

            var computer = new Computer
            {
                IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
            };
            computer.Open();
            computer.Accept(new UpdateVisitor());

            foreach (IHardware hw in computer.Hardware)
            {
                Console.WriteLine($"# {hw.HardwareType}: {hw.Name}");
                foreach (ISensor s in hw.Sensors)
                    Console.WriteLine($"    [{s.SensorType,-12}] {s.Name,-28} = {s.Value}");
                foreach (IHardware sub in hw.SubHardware)
                {
                    Console.WriteLine($"  ## sub: {sub.Name}");
                    foreach (ISensor s in sub.Sensors)
                        Console.WriteLine($"      [{s.SensorType,-12}] {s.Name,-28} = {s.Value}");
                }
            }
            computer.Close();
            Console.WriteLine();
            Console.WriteLine("Done. Copy the sensor names you want into orokin-config notes if auto-detect missed any.");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();
        private static void AllocConsoleIfNeeded()
        {
            try { AllocConsole(); } catch { }
        }
    }
}
