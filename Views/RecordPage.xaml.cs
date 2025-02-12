using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Project_FREAK.Views
{
    /// Interaction logic for RecordPage.xaml
    public partial class RecordPage : Page
    {
        public RecordPage()
        {
            InitializeComponent();

            //begin to subscribe to labjack data updates through action
            LabJackHandleManager.Instance.DataUpdated += UpdateGraphs;
        }
        //thrust in N, pressure in PSI
        private void UpdateGraphs(double thrustVoltage, double calibratedThrust, double pressureVoltage, double calibratedPressure)
        {
            //on an update, invoke ui thread to update with correct values. This will be replaced later with graphs of data.
            Dispatcher.Invoke(() => {
                Graph1.Text = $"Thrust: {calibratedThrust:F2} N";
                Graph2.Text = $"Pressure: {pressureVoltage:F2} PSI";
            });
        }
    }
}
