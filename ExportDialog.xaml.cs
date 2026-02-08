using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;

namespace YoloAnnotator
{
    public partial class ExportDialog : Window
    {
        private List<string> _imageNames;
        private List<string> _classes;

        public string SelectedFormat { get; private set; } = "yolo";
        public string ExportPath { get; private set; } = string.Empty;

        public ExportDialog(List<string> imageNames, List<string> classes)
        {
            InitializeComponent();
            _imageNames = imageNames ?? new List<string>();
            _classes = classes ?? new List<string>();
            
            UpdateStatistics();
        }

        public void SetSelectedFormat(string format)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in CboFormat.Items)
            {
                if (item.Tag?.ToString() == format)
                {
                    CboFormat.SelectedItem = item;
                    break;
                }
            }
        }

        private void UpdateStatistics()
        {
            TxtStatistics.Text = $"将导出 {_imageNames.Count} 张图片，{_classes.Count} 个类别";
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择导出目录",
                InitialDirectory = TxtExportPath.Text
            };

            if (dialog.ShowDialog() == true)
            {
                TxtExportPath.Text = dialog.FolderName;
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var exportPath = TxtExportPath.Text.Trim();
            
            if (string.IsNullOrEmpty(exportPath))
            {
                MessageBox.Show("请选择导出路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CboFormat.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag != null)
            {
                SelectedFormat = item.Tag.ToString()!;
            }

            ExportPath = exportPath;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}