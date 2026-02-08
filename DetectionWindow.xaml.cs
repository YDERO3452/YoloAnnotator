using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using YoloAnnotator.Models;
using YoloAnnotator.Services;

namespace YoloAnnotator
{
    public partial class DetectionWindow : Window
    {
        private readonly ApiService _apiService;
        private BitmapImage? _currentImage;
        private List<BoundingBox> _detectionResults = new List<BoundingBox>();

        public DetectionWindow()
        {
            InitializeComponent();
            _apiService = new ApiService();
            LoadModelInfo();
        }

        private async void LoadModelInfo()
        {
            try
            {
                var models = await _apiService.GetModelsAsync();
                if (models != null && models.Count > 0)
                {
                    TxtModelInfo.Text = $"当前模型: {models[0].Name}";
                }
            }
            catch
            {
                TxtModelInfo.Text = "当前模型: 未加载";
            }
        }

        // 窗口控制
        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                BtnMaximize.Visibility = Visibility.Visible;
                BtnRestore.Visibility = Visibility.Collapsed;
            }
            else
            {
                WindowState = WindowState.Maximized;
                BtnMaximize.Visibility = Visibility.Collapsed;
                BtnRestore.Visibility = Visibility.Visible;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // 选择图片
        private void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
                Title = "选择图片"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _currentImage = new BitmapImage(new Uri(dialog.FileName));
                    CurrentImage.Source = _currentImage;
                    DetectionCanvas.Width = _currentImage.PixelWidth;
                    DetectionCanvas.Height = _currentImage.PixelHeight;
                    
                    TxtStatus.Text = $"已加载图片: {System.IO.Path.GetFileName(dialog.FileName)}";
                    
                    // 清空之前的结果
                    _detectionResults.Clear();
                    ResultCanvas.Children.Clear();
                    LstResults.ItemsSource = null;
                    TxtResultCount.Text = "检测到 0 个目标";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"加载图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // 开始检测
        private async void BtnDetect_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("请先选择图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                TxtStatus.Text = "检测中...";
                BtnDetect.IsEnabled = false;

                // 这里应该调用实际的检测API
                // 暂时使用空列表
                _detectionResults = new List<BoundingBox>();
                
                // TODO: 实际的检测调用
                // _detectionResults = await _apiService.DetectAsync(imagePath);

                DrawDetectionResults();
                LstResults.ItemsSource = _detectionResults;
                TxtResultCount.Text = $"检测到 {_detectionResults.Count} 个目标";
                TxtStatus.Text = "检测完成";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"检测失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtStatus.Text = "检测失败";
            }
            finally
            {
                BtnDetect.IsEnabled = true;
            }
        }

        // 绘制检测结果
        private void DrawDetectionResults()
        {
            ResultCanvas.Children.Clear();

            if (_currentImage == null) return;

            foreach (var bbox in _detectionResults)
            {
                var color = GetColorForClass(bbox.ClassName);
                var brush = new SolidColorBrush(color);

                // 转换归一化坐标到像素坐标
                var x = (bbox.X - bbox.Width / 2) * _currentImage.PixelWidth;
                var y = (bbox.Y - bbox.Height / 2) * _currentImage.PixelHeight;
                var width = bbox.Width * _currentImage.PixelWidth;
                var height = bbox.Height * _currentImage.PixelHeight;

                // 绘制边界框
                var rect = new Rectangle
                {
                    Stroke = brush,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
                    Width = width,
                    Height = height
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                ResultCanvas.Children.Add(rect);

                // 绘制标签
                var label = new TextBlock
                {
                    Text = $"{bbox.ClassName} {bbox.Confidence:P0}",
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                    Padding = new Thickness(4, 2, 4, 2),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };

                Canvas.SetLeft(label, x);
                Canvas.SetTop(label, y - 20);
                ResultCanvas.Children.Add(label);
            }
        }

        // 清空结果
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _detectionResults.Clear();
            ResultCanvas.Children.Clear();
            LstResults.ItemsSource = null;
            TxtResultCount.Text = "检测到 0 个目标";
            TxtStatus.Text = "已清空结果";
        }

        // 导出结果
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_detectionResults.Count == 0)
            {
                MessageBox.Show("没有可导出的结果", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show("导出功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 根据类别名称生成颜色
        private Color GetColorForClass(string className)
        {
            var hash = className.GetHashCode();
            var r = (byte)((hash & 0xFF0000) >> 16);
            var g = (byte)((hash & 0x00FF00) >> 8);
            var b = (byte)(hash & 0x0000FF);
            
            // 确保颜色足够亮
            if (r + g + b < 200)
            {
                r = (byte)Math.Min(255, r + 100);
                g = (byte)Math.Min(255, g + 100);
                b = (byte)Math.Min(255, b + 100);
            }
            
            return Color.FromRgb(r, g, b);
        }
    }
}
