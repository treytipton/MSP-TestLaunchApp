using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.WPF;
using ScottPlot.Plottables;
using System.Text.Json;
using System.IO;
using Microsoft.Win32;

namespace Project_FREAK.Views
{
    public partial class RecordPage : Page
    {
        private SensorCheckWindow? _sensorCheckWindow;
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _loadingCts;
        private DispatcherTimer _timer;
        private bool _timerActive = false;
        private int _secondsRemaining = 5;
        private DateTime _startTime = DateTime.Now;

        private List<double> _timeData = new List<double>();
        private List<double> _thrustData = new List<double>();
        private List<double> _pressureData = new List<double>();

        private const double WindowSize = 10; // Sliding window size in seconds

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        public RecordPage()
        {
            InitializeComponent();
            LabJackHandleManager.Instance.DataUpdated += UpdateGraphs;
            this.Loaded += RecordPage_Loaded;
            this.Unloaded += RecordPage_Unloaded;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            ThrustDataLogger = ThrustGraph.Plot.Add.DataLogger();
            ThrustDataLogger.ViewSlide(windowSize);
            ThrustGraph.Plot.Axes.Bottom.Label.Text = "Time (s)";
            ThrustGraph.Plot.Axes.Left.Label.Text = "Thrust (N)";
            ThrustGraph.Plot.Title("Thrust Over Time");
            ThrustGraph.Plot.Axes.AutoScaleY();  // Auto-scale the Y-axis
            PressureDataLogger = PressureGraph.Plot.Add.DataLogger();
            PressureDataLogger.ViewSlide(windowSize);
            PressureGraph.Plot.Axes.Bottom.Label.Text = "Time (s)";
            PressureGraph.Plot.Axes.Left.Label.Text = "Pressure (PSI)";
            PressureGraph.Plot.Title("Pressure Over Time");
            PressureGraph.Plot.Axes.AutoScaleY();  // Auto-scale the Y-axis
            UpdatePlotTimer = new DispatcherTimer();
            UpdatePlotTimer.Interval = TimeSpan.FromMilliseconds(50);
            UpdatePlotTimer.Tick += (s, e) =>
            {
                PressureGraph.Refresh();
                ThrustGraph.Refresh();
            };
            UpdatePlotTimer.Start();
        }

        //thrust in N, pressure in PSI
        private void UpdateGraphs(double thrustVoltage, double calibratedThrust, double pressureVoltage, double calibratedPressure)
        {
            if (_isSaving) return; // Prevent updates while saving
            elapsedTime = (DateTime.Now - startTime).TotalSeconds;
            // Store data points
            timeData.Add(elapsedTime);
            thrustData.Add(calibratedThrust);
            pressureData.Add(calibratedPressure);
            rawPressureData.Add(pressureVoltage);
            rawThrustData.Add(thrustVoltage);

            Dispatcher.Invoke(() =>
            {
                if (_isSaving) return; // Double-check before updating UI
                // Update Thrust Graph
                ThrustDataLogger.Add(elapsedTime, calibratedThrust);

                // Update Pressure Graph
                PressureDataLogger.Add(elapsedTime, calibratedPressure);
            });
        }

        private void ExportDataToJson(string filePath)
        {
            var data = new
            {
                time_values_seconds = timeData,
                thrust_values_N = thrustData,
                pressure_values_PSI = pressureData,
                load_cell_voltages_mv = rawThrustData,  // Assuming thrust data corresponds to load cell voltages (in mV)
                pressure_transducer_voltages_v = rawPressureData // Assuming pressure data corresponds to transducer voltages (in V)
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(data, options);

            File.WriteAllText(filePath, json);
        }
        // Load the webcam input on a background thread and start the loading text animation.
        private async void RecordPage_Loaded(object sender, RoutedEventArgs e)
        {
            plot.Plot.Clear();
            plot.Plot.Add.Scatter(xData.ToArray(), yData.ToArray());
            plot.Plot.Axes.Bottom.Label.Text = "Time (s)";
            plot.Plot.Axes.Left.Label.Text = yLabel;
            plot.Plot.Title(title);
            plot.Plot.Axes.SetLimitsX(Math.Max(0, xData.Last() - WindowSize), xData.Last());
            plot.Refresh();
        }

        // Handles the page loaded event to initialize the webcam and start the capture loop.
        private async void RecordPage_Loaded(object sender, RoutedEventArgs e)
        {
            _loadingCts = new CancellationTokenSource();
            var loadingTask = AnimateLoadingText(_loadingCts.Token);

            try
            {
                await Task.Run(() => InitializeWebcam());
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LoadingTextBlock.Text = $"Error: {ex.Message}");
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (_capture?.IsOpened() == true)
                {
                    LoadingTextBlock.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LoadingTextBlock.Text = "Webcam Error";
                }
            });

            if (_capture?.IsOpened() == true)
            {
                _cts = new CancellationTokenSource();
                // Start async capture loop
                _ = CaptureLoop(_cts.Token);
            }
        }

        // Animates the loading text while the webcam is being initialized.
        private async Task AnimateLoadingText(CancellationToken token)
        {
            string[] loadingTexts = { "Loading.", "Loading..", "Loading..." };
            int index = 0;

            while (!token.IsCancellationRequested)
            {
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    LoadingTextBlock.Text = loadingTexts[index % loadingTexts.Length];
                }));
                index++;
                try
                {
                    await Task.Delay(350, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        // Initializes the webcam.
        private void InitializeWebcam()
        {
            string rtspUrl = "rtsp://admin:MSPMOTORTEST2025@192.168.20.4:554/cam/realmonitor?channel=1&subtype=0";

            _capture = new VideoCapture(rtspUrl); // 0 for default camera, use a different number for other cameras.
            if (_capture is null || !_capture.IsOpened())
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingTextBlock.Text = "No RTSP stream Found";
                });
                _loadingCts?.Cancel();
                _capture?.Dispose();
                _capture = null;
            }
            _capture.Set(VideoCaptureProperties.BufferSize, 1);
        }

        // Starts the capture loop to continuously capture frames from the webcam.
        private async void StartCaptureLoop()
        {
            if (_capture == null)
                return;

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            double cameraFps = _capture.Get(VideoCaptureProperties.Fps);
            if (cameraFps <= 0)
                cameraFps = 30;
            int delay = (int)(1000 / cameraFps);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_capture == null || !_capture.IsOpened())
                        break;

                    using (var frame = new Mat())
                    {
                        //_capture.Read(frame); // Capture a frame
                        _capture.Grab();
                        _capture.Retrieve(frame);
                        if (!frame.Empty())
                        {
                            using (var bmp = frame.ToBitmap())
                            {
                                IntPtr hBitmap = bmp.GetHbitmap();
                                try
                                {
                                    BitmapSource bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                                        hBitmap,
                                        IntPtr.Zero,
                                        Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());
                                    await Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        WebcamImage.Source = bitmap;
                                    }));
                                }
                                finally
                                {
                                    DeleteObject(hBitmap);
                                }
                            }
                        }
                    }
                    await Task.Delay(delay, token);
                }
            }
            catch (TaskCanceledException)
            {
                // Capture loop canceled; exit gracefully.
            }
        }

        // Handles the page unloaded event to clean up resources.
        private void RecordPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _loadingCts?.Cancel();

            if (_capture != null)
            {
                _capture.Release();
                _capture.Dispose();
                _capture = null;
            }

            // Clear image source to prevent memory leaks
            WebcamImage.Source = null;
        }

        // Handles the Sensor Check button click event to open the SensorCheckWindow.
        private void SensorCheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sensorCheckWindow == null)
            {
                _sensorCheckWindow = new SensorCheckWindow();
                _sensorCheckWindow.Closed += (s, args) =>
                {
                    _sensorCheckWindow = null;
                    Dispatcher.Invoke(() =>
                    {
                        LabJackHandleManager.Instance.DataUpdated -= UpdateGraphs;
                        LabJackHandleManager.Instance.DataUpdated += UpdateGraphs;
                    });
                };
                _sensorCheckWindow.Show();
            }
            else
            {
                if (_sensorCheckWindow.WindowState == WindowState.Minimized)
                {
                    _sensorCheckWindow.WindowState = WindowState.Normal;
                }
                _sensorCheckWindow.Activate();
            }
        }

        // Handles the Arm button click event to arm or disarm the igniter.
        private async void ArmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isArmed = LabJackHandleManager.Instance.GetArmedStatus();
                await Task.Run(() => LabJackHandleManager.Instance.ArmDisarmIgniter());

                Dispatcher.Invoke(() =>
                {
                    if (isArmed)
                    {
                        ArmButton.Background = Brushes.Green;
                        ArmTextBlock.Text = "Arm";
                        StartTestTextBlock.Text = "Start";
                        StartTestTextBlock.TextDecorations = TextDecorations.Strikethrough;
                        StartButton.Background = Brushes.DarkGray;
                        _timer.Stop();
                        _timerActive = false;
                    }
                    else
                    {
                        ArmButton.Background = Brushes.Red;
                        StartButton.Background = Brushes.Orange;
                        StartTestTextBlock.TextDecorations = null;
                        ArmTextBlock.Text = "ARMED";
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"Error: {ex.Message}"));
            }
        }

        // Handles the Start Test button click event to start or stop the countdown timer.
        private void StartTestButton_Click(object sender, RoutedEventArgs e)
        {
            if (_timerActive)
            {
                _timer.Stop();
                StartTestTextBlock.Text = "Start";
                _timerActive = false;
            }
            else if (!_timerActive && LabJackHandleManager.Instance.GetArmedStatus())
            {
                _secondsRemaining = 5;
                _timerActive = true;
                StartTestTextBlock.Text = $"00:{_secondsRemaining}";
                _timer.Start();
            }
        }

        // Handles the timer tick event to update the countdown and ignite the motor.
        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_secondsRemaining > 0)
            {
                _secondsRemaining--;
                StartTestTextBlock.Text = $"00:{_secondsRemaining}";
            }
            else
            {
                _timer.Stop();
                _timerActive = false;
                StartTestTextBlock.Text = "Igniting Motor!";
                Task.Run(() => LabJackHandleManager.Instance.IgniteMotor());
            }
        }

        private async Task CaptureLoop(CancellationToken token)
        {
            if (_capture == null)
                return;

            double cameraFps = _capture.Get(VideoCaptureProperties.Fps);
            if (cameraFps <= 0)
                cameraFps = 30;
            int delay = (int)(1000 / cameraFps);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_capture == null || !_capture.IsOpened())
                        break;

                    using (var frame = new Mat())
                    {
                        _capture.Read(frame);
                        if (!frame.Empty())
                        {
                            using (var bmp = frame.ToBitmap())
                            {
                                IntPtr hBitmap = bmp.GetHbitmap();
                                try
                                {
                                    BitmapSource bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                                        hBitmap,
                                        IntPtr.Zero,
                                        Int32Rect.Empty,
                                        BitmapSizeOptions.FromEmptyOptions());

                                    // Freeze the bitmap to enable cross-thread access
                                    bitmap.Freeze();

                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        WebcamImage.Source = bitmap;
                                    });
                                }
                                finally
                                {
                                    DeleteObject(hBitmap);
                                }
                            }
                        }
                    }
                    await Task.Delay(delay, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Prevent updates to the graphs during saving
            _isSaving = true;

            // Create and show SaveFileDialog for selecting file path
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                Title = "Save Data as JSON",
                FileName = "data_export.json"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                // Export the data to JSON file
                ExportDataToJson(filePath);
                MessageBox.Show("Data successfully saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Re-enable graph updates after saving
            _isSaving = false;
        }
    }
}
