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
        private VideoCapture? _capture; // VideoCapture object to access the webcam
        private CancellationTokenSource? _cts; // Token source to cancel the capture loop
        private CancellationTokenSource? _loadingCts; // Token source to stop loading animation

        private DispatcherTimer _timer;
        private bool _timerActive = false;
        private int _secondsremaining = 5; //5 sec countdown by default
        // Importing the DeleteObject function from the gdi32.dll to release GDI objects (like HBITMAPs) in unmanaged code.
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
        private DateTime startTime = DateTime.Now;
        private Double elapsedTime;
        private DataLogger ThrustDataLogger;
        private DataLogger PressureDataLogger;
        private DispatcherTimer UpdatePlotTimer;
        private List<double> timeData = new List<double>();
        private List<double> thrustData = new List<double>();
        private List<double> rawThrustData = new List<double>();
        private List<double> rawPressureData = new List<double>();
        private List<double> pressureData = new List<double>();
        private bool _isSaving = false;


        private const double windowSize = 10; // Sliding window size (e.g., 10 seconds)
        public RecordPage()
        {
            InitializeComponent();
            //begin to subscribe to labjack data updates through action
            LabJackHandleManager.Instance.DataUpdated += UpdateGraphs;
            // Load webcam feed separately from the UI thread.
            this.Loaded += RecordPage_Loaded;
            this.Unloaded += RecordPage_Unloaded;
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
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
            _loadingCts = new CancellationTokenSource(); // Create a new cancellation token source

            // Start the loading text animation asynchronously.
            var loadingTask = AnimateLoadingText(_loadingCts.Token);

            // Initialize the webcam on a background thread.
            await Task.Run(() => InitializeWebcam());

            // If the webcam was successfully initialized, hide the loading text.
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_capture != null && _capture.IsOpened())
                {
                    LoadingTextBlock.Visibility = Visibility.Collapsed;
                }
            }));

            // Start the capture loop only if the webcam is available.
            if (_capture != null && _capture.IsOpened())
            {
                StartCaptureLoop();
            }
        }

        // Animates the LoadingTextBlock until the webcam is ready or not found.
        private async Task AnimateLoadingText(CancellationToken token)
        {
            string[] loadingTexts = { "Loading.", "Loading..", "Loading..." };
            int index = 0;

            while (!token.IsCancellationRequested) // Stop when cancellation is requested
            {
                await Dispatcher.BeginInvoke(new Action(() =>
                {
                    LoadingTextBlock.Text = loadingTexts[index % loadingTexts.Length];
                }));
                index++;
                try
                {
                    await Task.Delay(350, token); // Allow cancellation
                }
                catch (TaskCanceledException)
                {
                    break; // Exit if canceled
                }
            }
        }

        // Initialize the webcam.
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

                _loadingCts?.Cancel(); // Stop the loading animation
                return;
            }
            _capture.Set(VideoCaptureProperties.BufferSize, 1);
        }

        // Continuously capture frames from the webcam and update the UI.
        private async void StartCaptureLoop()
        {
            if (_capture is null)
                return;

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            // Query the camera's FPS; default to 30 if the value is invalid.
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
                                    // Update the UI asynchronously.
                                    await Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        WebcamImage.Source = bitmap;
                                    }));
                                }
                                finally
                                {
                                    DeleteObject(hBitmap); // Free the unmanaged HBitmap
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

        // Cancel the capture loop and dispose of the webcam resources when the page unloads.
        private void RecordPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();

            if (_capture != null)
            {
                _capture.Release();
                _capture.Dispose();
                _capture = null;
            }
        }

        private void SensorCheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sensorCheckWindow == null)
            {
                _sensorCheckWindow = new SensorCheckWindow();
                _sensorCheckWindow.Closed += (s, args) =>
                {
                    _sensorCheckWindow = null;

                    // Force resubscription and refresh graphs when the window closes
                    Dispatcher.Invoke(() =>
                    {
                        LabJackHandleManager.Instance.DataUpdated -= UpdateGraphs; // Prevent duplicate subscriptions
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
        private void ArmButton_Click(object sender, RoutedEventArgs e)
        {
            //if armed, we need to disarm
            if(LabJackHandleManager.Instance.GetArmedStatus())
            {
                LabJackHandleManager.Instance.ArmDisarmIgniter();
                ArmButton.Background = Brushes.Green;
                ArmTextBlock.Text = "Arm";
                StartTestTextBlock.Text = "Start";
                StartTestTextBlock.TextDecorations = TextDecorations.Strikethrough;
                StartButton.Background = Brushes.DarkGray;
                //lets also check if countdown has started, and cancel it if it has
                if (_timerActive)
                {
                    _timer.Stop();
                    _timerActive = false;

                }
            }
            else
            {
                //if disarmed, we need to arm
                LabJackHandleManager.Instance.ArmDisarmIgniter();
                ArmButton.Background = Brushes.Red;
                StartButton.Background = Brushes.Orange;
                StartTestTextBlock.TextDecorations = null;
                ArmTextBlock.Text = "ARMED";
            }
        }
        private void StartTestButton_Click(object sender, RoutedEventArgs e)
        {
            if(_timerActive)
            {
                _timer.Stop();
                StartTestTextBlock.Text = "Start";
                _timerActive = false;
            }
            else if (_timerActive == false && LabJackHandleManager.Instance.GetArmedStatus())
            {
                _secondsremaining = 5;
                _timerActive = true;
                StartTestTextBlock.Text = $"00:{_secondsremaining}";
                _timer.Start();
            }
        }
        private void Timer_Tick(object sender, EventArgs e)
        {
            if(_secondsremaining > 0 )
            {
                _secondsremaining--;
                StartTestTextBlock.Text = $"00:{_secondsremaining}";
            }
            else
            {
                _timer.Stop();
                _timerActive = false;
                //begin ignition
                StartTestTextBlock.Text = "Igniting Motor!";
                LabJackHandleManager.Instance.IgniteMotor();
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
