using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace HRVMonitoringSystem
{
    public partial class PortSelectionDialog : Window
    {
        public ComPortInfo SelectedPort { get; private set; }

        public PortSelectionDialog(List<ComPortInfo> ports)
        {
            InitializeComponent();

            portListBox.ItemsSource = ports;

            // Wire up events in code
            connectButton.Click += (s, e) =>
            {
                SelectedPort = portListBox.SelectedItem as ComPortInfo;
                DialogResult = true;
            };

            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
            };

            portListBox.MouseDoubleClick += (s, e) =>
            {
                if (portListBox.SelectedItem != null)
                {
                    SelectedPort = portListBox.SelectedItem as ComPortInfo;
                    DialogResult = true;
                }
            };
        }
    }
}