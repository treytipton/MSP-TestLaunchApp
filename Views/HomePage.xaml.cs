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
    /// Interaction logic for HomePage.xaml
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
        }

        // Event handlers for the buttons on the home page
        private void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = (MainWindow)Application.Current.MainWindow;
            mainWindow.NavigateToPage(new RecordPage(), "Record");
        }

        private void ReplayButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = (MainWindow)Application.Current.MainWindow;
            mainWindow.NavigateToPage(new ReplayPage(), "Replay");
        }
    }
}
