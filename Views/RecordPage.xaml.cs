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
using ScottPlot;
using ScottPlot.WPF;
using ScottPlot.Plottables;



namespace Project_FREAK.Views
{
    public partial class RecordPage : Page
    {
        private SensorCheckWindow? _sensorCheckWindow;
        private VideoCapture? _capture; // VideoCapture object to access the webcam
        private CancellationTokenSource? _cts; // Token source to cancel the capture loop
        private CancellationTokenSource? _loadingCts; // Token source to stop loading animation

        // Importing the DeleteObject function from the gdi32.dll to release GDI objects (like HBITMAPs) in unmanaged code.
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
        private DateTime startTime = DateTime.Now;
        public RecordPage()
        {
            InitializeComponent();
            //begin to subscribe to labjack data updates through action
            LabJackHandleManager.Instance.DataUpdated += UpdateGraphs;
            // Load webcam feed separately from the UI thread.
            this.Loaded += RecordPage_Loaded;
            this.Unloaded += RecordPage_Unloaded;
        }
        //thrust in N, pressure in PSI
        private List<double> timeData = new List<double>();
        private List<double> thrustData = new List<double>();
        private List<double> pressureData = new List<double>();

        private const double WindowSize = 10; // Sliding window size (e.g., 10 seconds)
        private void UpdateGraphs(double thrustVoltage, double calibratedThrust, double pressureVoltage, double calibratedPressure)
        {
            double elapsedTime = (DateTime.Now - startTime).TotalSeconds;

            // Store data points
            timeData.Add(elapsedTime);
            thrustData.Add(calibratedThrust);
            pressureData.Add(calibratedPressure);

            // Remove old data to maintain the sliding window
            while (timeData.Count > 0 && timeData[0] < elapsedTime - WindowSize)
            {
                timeData.RemoveAt(0);
                thrustData.RemoveAt(0);
                pressureData.RemoveAt(0);
            }
            Dispatcher.Invoke(() =>
            {
                // Update Thrust Graph
                ThrustGraph.Plot.Clear();
                ThrustGraph.Plot.Add.Scatter(timeData.ToArray(), thrustData.ToArray());
                ThrustGraph.Plot.Axes.Bottom.Label.Text = "Time (s)";
                ThrustGraph.Plot.Axes.Left.Label.Text = "Thrust (N)";
                ThrustGraph.Plot.Title("Thrust Over Time");
                ThrustGraph.Plot.Axes.SetLimitsX(Math.Max(0, elapsedTime - WindowSize), elapsedTime);
                ThrustGraph.Refresh();

                // Update Pressure Graph
                PressureGraph.Plot.Clear();
                PressureGraph.Plot.Add.Scatter(timeData.ToArray(), pressureData.ToArray());
                PressureGraph.Plot.Axes.Bottom.Label.Text = "Time (s)";
                PressureGraph.Plot.Axes.Left.Label.Text = "Pressure (PSI)";
                PressureGraph.Plot.Title("Pressure Over Time");
                PressureGraph.Plot.Axes.SetLimitsX(Math.Max(0, elapsedTime - WindowSize), elapsedTime);
                PressureGraph.Refresh();
            });
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
            _capture = new VideoCapture(0); // 0 for default camera, use a different number for other cameras.
            if (_capture is null || !_capture.IsOpened())
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingTextBlock.Text = "No Webcam Found";
                });

                _loadingCts?.Cancel(); // Stop the loading animation
                return;
            }
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
                        _capture.Read(frame); // Capture a frame
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
                _sensorCheckWindow.Closed += (s, args) => _sensorCheckWindow = null;
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
    }
}
