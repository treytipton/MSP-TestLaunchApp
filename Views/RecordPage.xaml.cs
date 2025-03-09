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
        }

        // Updates the graphs with new data points.
        private void UpdateGraphs(double thrustVoltage, double calibratedThrust, double pressureVoltage, double calibratedPressure)
        {
            double elapsedTime = (DateTime.Now - _startTime).TotalSeconds;

            // Store data points
            _timeData.Add(elapsedTime);
            _thrustData.Add(calibratedThrust);
            _pressureData.Add(calibratedPressure);

            // Remove old data to maintain the sliding window
            while (_timeData.Count > 0 && _timeData[0] < elapsedTime - WindowSize)
            {
                _timeData.RemoveAt(0);
                _thrustData.RemoveAt(0);
                _pressureData.RemoveAt(0);
            }

            // Update the graphs on the UI thread
            Dispatcher.Invoke(() =>
            {
                UpdateGraph(ThrustGraph, _timeData, _thrustData, "Thrust (N)", "Thrust Over Time");
                UpdateGraph(PressureGraph, _timeData, _pressureData, "Pressure (PSI)", "Pressure Over Time");
            });
        }

        // Updates a specific graph with new data.
        private void UpdateGraph(WpfPlot plot, List<double> xData, List<double> yData, string yLabel, string title)
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
            try
            {
                _capture = new VideoCapture(0);
                if (!_capture.IsOpened())
                {
                    throw new InvalidOperationException("Webcam not accessible");
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingTextBlock.Text = ex.Message;
                });
                _loadingCts?.Cancel();
                _capture?.Dispose();
                _capture = null;
            }
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
    }
}
