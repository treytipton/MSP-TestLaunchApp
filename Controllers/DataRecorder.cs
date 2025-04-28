using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using ScottPlot;

namespace Project_FREAK.Controllers
{
    public class DataRecorder
    {
        // Lists to store recorded data
        public List<double> TimeData { get; } = new();
        public List<double> ThrustData { get; } = new();
        public List<double> PressureData { get; } = new();
        public List<double> RawThrustVoltages { get; } = new();
        public List<double> RawPressureVoltages { get; } = new();

        private StreamWriter? _dataWriter;
        private string? _tempDataPath;

        private record DataPoint(
            double time,
            double thrust,
            double pressure,
            double thrustVoltage,
            double pressureVoltage
        );

        // Adds a data point to the lists
        public void AddDataPoint(double time, double thrust, double pressure,
                               double thrustVoltage, double pressureVoltage)
        {
            var dataPoint = new
            {
                time,
                thrust,
                pressure,
                thrustVoltage,
                pressureVoltage
            };

            _dataWriter?.WriteLine(JsonSerializer.Serialize(dataPoint));
            _dataWriter?.Flush(); // Ensure data is written immediately
        }

        // Exports the collected data to final JSON format
        public void ExportToJson(string finalPath)
        {
            try
            {
                if (_tempDataPath == null || !File.Exists(_tempDataPath)) return;

                // Add retry logic for file access
                const int maxRetries = 5;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // Read and process data
                        var lines = File.ReadAllLines(_tempDataPath);

                        // Deserialize and organize data
                        var data = new
                        {
                            time_values_seconds = lines.Select(l => JsonSerializer.Deserialize<DataPoint>(l).time).ToList(),
                            thrust_values_N = lines.Select(l => JsonSerializer.Deserialize<DataPoint>(l).thrust).ToList(),
                            pressure_values_PSI = lines.Select(l => JsonSerializer.Deserialize<DataPoint>(l).pressure).ToList(),
                            load_cell_voltages_mv = lines.Select(l => JsonSerializer.Deserialize<DataPoint>(l).thrustVoltage).ToList(),
                            pressure_transducer_voltages_v = lines.Select(l => JsonSerializer.Deserialize<DataPoint>(l).pressureVoltage).ToList()
                        };

                        // Write final JSON
                        File.WriteAllText(finalPath, JsonSerializer.Serialize(data,
                            new JsonSerializerOptions { WriteIndented = true }));
                        // ... rest of export logic ...
                        break;
                    }
                    catch (IOException) when (i < maxRetries - 1)
                    {
                        Thread.Sleep(50); // Wait for file lock to release
                    }
                }
            }
            finally
            {
                try { File.Delete(_tempDataPath); } catch { /* Ignore cleanup errors */ }
            }
        }

        // Creates a temporary data file and initializes the writer
        public void StartRecording(string sessionFolder)
        {
            StopRecording(); // Ensure any previous writer is closed
            _tempDataPath = Path.Combine(sessionFolder, $"temp_data_{Guid.NewGuid()}.jsonl");
            _dataWriter = new StreamWriter(_tempDataPath, append: false)
            {
                AutoFlush = true // Ensure automatic flushing
            };
        }

        public void Dispose()
        {
            _dataWriter?.Dispose();
        }

        public void StopRecording()
        {
            _dataWriter?.Flush();
            _dataWriter?.Dispose();
            _dataWriter = null;
        }
    }
}