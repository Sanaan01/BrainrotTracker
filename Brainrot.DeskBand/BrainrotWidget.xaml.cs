using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Brainrot.DeskBand
{
    public partial class BrainrotWidget : UserControl
    {
        public BrainrotWidget()
        {
            InitializeComponent();
        }

        public void Update(string label, string emoji, double focusPercent)
        {
            StateLabel.Text = label;
            StateEmoji.Text = emoji;
            FocusPercent.Text = $"{(int)focusPercent}%";

            // Color code based on focus percentage
            if (focusPercent >= 80)
            {
                FocusPercent.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xE2, 0x4A)); // Green
            }
            else if (focusPercent >= 50)
            {
                FocusPercent.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)); // Blue
            }
            else if (focusPercent >= 20)
            {
                FocusPercent.Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xA8, 0x4A)); // Orange
            }
            else
            {
                FocusPercent.Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0x4A, 0x4A)); // Red
            }
        }
    }
}
