using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LabJack;

namespace Project_FREAK
{
    public class LabJackHandleManager
    {
        private int _handle = -1;
        private bool _isDemo = true;
        private static LabJackHandleManager _instance;
        private LabJackHandleManager()
        {
            InitalizeLabJack();
        }
        public static LabJackHandleManager Instance => _instance ??= new LabJackHandleManager(); //singleton, only one can be created.

        private void InitalizeLabJack()
        {
            try
            {
                LJM.OpenS("ANY", "ANY", "ANY", ref _handle);
                _isDemo = false;
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
        public void CloseDevice()
        {
            if (_handle > 0)
            {
                LJM.Close(_handle);
                _handle = -1;
            }
        }
    }
}
