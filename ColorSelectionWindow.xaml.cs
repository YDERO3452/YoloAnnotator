using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YoloAnnotator
{
    public partial class ColorSelectionWindow : Window
    {
        public string SelectedColor { get; private set; } = string.Empty;

        private readonly List<string> _colorList = new List<string>
        {
            "#ef4444", "#22c55e", "#3b82f6", "#f59e0b", "#8b5cf6",
            "#ec4899", "#06b6d4", "#84cc16", "#f97316", "#14b8a6",
            "#a855f7", "#eab308", "#6366f1", "#10b981", "#f43f5e",
            "#8b5a2b", "#2dd4bf", "#a3e635", "#fb923c", "#f472b6",
            "#22d3d3", "#a78bfa", "#facc15", "#fb7185", "#4ade80",
            "#60a5fa", "#fbbf24", "#f87171", "#34d399", "#c084fc",
            "#fb923c", "#a5f3fc", "#86efac", "#fdba74", "#fca5a5",
            "#ff0000", "#00ff00", "#0000ff", "#ffff00", "#ff00ff",
            "#00ffff", "#ffffff", "#000000", "#808080", "#c0c0c0"
        };

        public ColorSelectionWindow()
        {
            InitializeComponent();
            ColorList.ItemsSource = _colorList;
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string color)
            {
                SelectedColor = color;
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedColor))
            {
                SelectedColor = "#22c55e"; // 默认绿色
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}