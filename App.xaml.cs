using System;
using System.Windows;

namespace HRVMonitoringSystem
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Add exception handlers
            this.Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            try
            {
                // Register Syncfusion license
                Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1NNaF5cXmBCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdmWXpcdHVdRGVfUE1zX0ZWYUA=");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"License Error: {ex.Message}\n\nThe application will continue without license.",
                    "Syncfusion License", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An error occurred:\n\n{e.Exception.Message}\n\nInner: {e.Exception.InnerException?.Message}",
                "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            MessageBox.Show($"Fatal error:\n\n{ex?.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}