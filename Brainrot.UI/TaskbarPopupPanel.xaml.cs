using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Brainrot.UI
{
    public sealed partial class TaskbarPopupPanel : UserControl
    {
        public event Action? CloseRequested;
        public event Action? OpenMainRequested;

        public TaskbarPopupPanel()
        {
            this.InitializeComponent();
        }

        public void Update(string label, string emoji, string focus, string neutral, string rot, double focusPct)
        {
            StatusEmoji.Text = emoji;
            
            // Calculate rot level (inverse of focus)
            int rotLevel = 100 - (int)focusPct;
            FocusPercentText.Text = $"Rot Level: {rotLevel}%";
            
            FocusTime.Text = focus;
            NeutralTime.Text = neutral;
            RotTime.Text = rot;
            
            // Update progress bar proportions based on times
            UpdateProgressBar(focus, neutral, rot);
        }
        
        private void UpdateProgressBar(string focus, string neutral, string rot)
        {
            int focusSec = ParseTimeToSeconds(focus);
            int neutralSec = ParseTimeToSeconds(neutral);
            int rotSec = ParseTimeToSeconds(rot);
            int total = focusSec + neutralSec + rotSec;
            
            if (total > 0)
            {
                FocusCol.Width = new GridLength(Math.Max(focusSec, 1), GridUnitType.Star);
                NeutralCol.Width = new GridLength(Math.Max(neutralSec, 1), GridUnitType.Star);
                RotCol.Width = new GridLength(Math.Max(rotSec, 1), GridUnitType.Star);
            }
        }
        
        private static int ParseTimeToSeconds(string time)
        {
            try
            {
                // Format: "Xh YYm" or "Xm YYs"
                time = time.ToLower().Trim();
                int total = 0;
                
                if (time.Contains("h"))
                {
                    var parts = time.Split('h');
                    total += int.Parse(parts[0].Trim()) * 3600;
                    time = parts.Length > 1 ? parts[1] : "";
                }
                if (time.Contains("m"))
                {
                    var parts = time.Split('m');
                    total += int.Parse(parts[0].Trim()) * 60;
                    time = parts.Length > 1 ? parts[1] : "";
                }
                if (time.Contains("s"))
                {
                    var parts = time.Split('s');
                    total += int.Parse(parts[0].Trim());
                }
                return total;
            }
            catch { return 0; }
        }

        private void OpenMain_Click(object sender, RoutedEventArgs e)
        {
            OpenMainRequested?.Invoke();
            CloseRequested?.Invoke();
        }
    }
}
