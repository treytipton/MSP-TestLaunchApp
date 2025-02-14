using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LabJack;
using System.Threading;
using System.Reflection.Metadata;
using System.Xml.Linq;

namespace Project_FREAK
{
    public class LabJackHandleManager
    {
        private int _handle = -1;
        private bool _isDemo = true;
        private static LabJackHandleManager _instance;
        private bool _isReading;
        public event Action<double, double, double, double> DataUpdated;
        private LabJackHandleManager()
        {
            InitalizeLabJack();
            StartReading();
        }
        public static LabJackHandleManager Instance => _instance ??= new LabJackHandleManager(); //singleton, only one can be created.

        private void InitalizeLabJack()
        {
            try
            {
                LJM.OpenS("ANY", "ANY", "ANY", ref _handle);
                _isDemo = false;
                //LabJack T4 configuration
                //    Resolution index = 0 (default)
                //    Settling = 0 (auto)
                string[] aNames = new string[] { "AIN0_RESOLUTION_INDEX", "AIN5_RESOLUTION_INDEX",
                        "AIN0_SETTLING_US", "AIN5_SETTLING_US" };
                double[] aValues = new double[] { 0, 0, 0, 0 };
                int numFrames = aNames.Length;
                int errorAddress = -1;
                LJM.eWriteNames(_handle, numFrames, aNames, aValues, ref errorAddress);
            }
            catch (LJM.LJMException exception)
            {
                LJM.OpenS("ANY", "ANY", "LJM_DEMO_MODE", ref _handle); //open in demo mode if device not found
                _isDemo = true;
            }
        }
        public int GetHandle()
        {
            return _handle;
        }
        public bool IsDemo() //check if currently running in demo mode (i.e. couldn't find a labjack device)
        {
            return _isDemo;
        }
        private void StartReading()
        {
            _isReading = true;
            // spin up a thread from the thread pool to read data from LabJack
            Task.Run(() =>
            {
                while (_isReading)
                {
                ReadData();
                }
            });
        }
        private void ReadData()
        {
            if (_handle > 0)
            {
                try
                {
                    //read in voltages from their specified input ports (TODO: make this customizable)
                    double thrustVoltage = 0, pressureVoltage = 0;
                    LJM.eReadName(_handle, "AIN5", ref thrustVoltage);
                    LJM.eReadName(_handle, "AIN0", ref pressureVoltage);

                    //convert voltage to thrust now...
                    //Load = RatedLoad *(VAIN- Voffset)/ (gain * Sensitivity * Vexc) in kilos
                    //our load cell's senitivity is 2 mV/V
                    //Excition voltage available on Input 6.
                    double Vexc = 0;
                    //1.25 and 201 taken from dip switches active on the LJ-Tick-InAmp. For more info see https://labjack.com/pages/support?doc=/datasheets/accessories/ljtick-inamp-datasheet/
                    double loadAdjVoltage = (thrustVoltage - 1.25) / 201;
                    LJM.eReadName(_handle, "AIN6", ref Vexc);
                    //use below lines to implement dynamic calibration. For now calibration values will be static
                    //load = 500 * (loadVoltage - 1.25) / (201 * 2 * Vexc)
                    //load = 500 * loadAdjVoltage / (2 * 2.5)
                    double calibratedLoad = calibratedLoad = 100387.5 * loadAdjVoltage - 3.8069375;
                    calibratedLoad *= 9.81; // convert from kg to N
                    //convert voltage to pressure
                    //pressures transducer 0.5 v = 0 PSI, 4.5 V = 1600 PSI, linear scaling
                    double pressureAdjustedVoltage = pressureVoltage - 0.5;
                    double pressure = pressureAdjustedVoltage * 4;
                    //invoke action, pass values onto subscribers
                    DataUpdated?.Invoke(thrustVoltage, calibratedLoad, pressureVoltage, pressure);
                }
                catch (Exception exception)
                {
                        Console.WriteLine($"Error reading values: {exception.Message}");
                }
            }
        }
        public void CloseDevice()
        {
            if (_handle > 0)
            {
                //stop reading, and close device
                _isReading = false;
                LJM.Close(_handle);
                _handle = -1;
            }
        }
    }
}
