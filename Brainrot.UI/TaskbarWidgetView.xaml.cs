using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Brainrot.UI
{
    public sealed partial class TaskbarWidgetView : UserControl
    {
        public TaskbarWidgetView()
        {
            this.InitializeComponent();
        }

        public void Update(string label, string emoji, string focus, string neutral, string rot, double focusPercent)
        {
            Title.Text = label;
            Glyph.Text = emoji;
            Percent.Text = $"{System.Math.Round(focusPercent)}% focus";
            Bar.Value = focusPercent;
        }
    }
}
