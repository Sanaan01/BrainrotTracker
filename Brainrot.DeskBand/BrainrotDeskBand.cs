using System;
using System.Runtime.InteropServices;
using System.Windows;
using CSDeskBand;

namespace Brainrot.DeskBand
{
    [ComVisible(true)]
    [Guid("B8A1F4E2-5C3D-4A2B-9E1F-6D8C7A5B4E3D")]
    [CSDeskBandRegistration(Name = "Brainrot Tracker", ShowDeskBand = true)]
    public class BrainrotDeskBand : CSDeskBandWpf
    {
        private const int DefaultWidth = 100;
        private readonly BrainrotWidget _widget;

        public BrainrotDeskBand()
        {
            _widget = new BrainrotWidget();

            Options.MinHorizontalSize = new DeskBandSize(DefaultWidth, 0);
            Options.HorizontalSize = new DeskBandSize(DefaultWidth, TaskbarInfo.Size.Height);
            Options.MinVerticalSize = new DeskBandSize(CSDeskBandOptions.TaskbarVerticalWidth, 40);
            Options.IsFixed = true;

            TaskbarInfo.TaskbarEdgeChanged += (s, e) =>
            {
                // Handle taskbar position changes
            };

            // Start a timer to update the widget
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            timer.Tick += (s, e) => UpdateWidget();
            timer.Start();
        }

        protected override UIElement UIElement => _widget;

        private void UpdateWidget()
        {
            // TODO: Get data from the main BrainrotTracker app
            // For now, just show placeholder data
            // You can use named pipes or a shared file to communicate
        }

        protected override void DeskbandOnClosed()
        {
            base.DeskbandOnClosed();
        }
    }
}
