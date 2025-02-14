using System.Configuration;
using System.Data;
using System.Windows;
using LabJack;
namespace Project_FREAK
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _ = LabJackHandleManager.Instance; //create LabJack device handle on startup
        }
        protected override void OnExit(ExitEventArgs e)
        {
            LabJackHandleManager.Instance.CloseDevice(); //close device when done with app
            base.OnExit(e);
        }
    }

}
