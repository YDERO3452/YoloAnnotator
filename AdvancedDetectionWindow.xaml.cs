using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using YoloAnnotator.Models;
using YoloAnnotator.Services;

namespace YoloAnnotator
{
    public partial class AdvancedDetectionWindow : Window
    {
        private readonly ApiService _apiService;
        private readonly string _imageName;
        private BitmapImage? _currentImage;
        private List<BoundingBox> _detectionResults = new List<BoundingBox>();
        private double _confidenceThreshold = 0.25;
        private double _iouThreshold = 0.45;

        public AdvancedDetectionWindow(string imageName)
        {
            InitializeComponent();
            _apiService = new ApiService();
            _imageName = imageName;
            LoadImage();
        }

        private async void LoadImage()
        {
            try
            {
                var imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend", "images", _imageName);
                
                if (File.Exists(imagePath))
                {
                    _currentImage = new BitmapImage(new Uri(imagePath));
                    CurrentImage.Source = _currentImage;
                    DetectionCanvas.Width = _currentImage.PixelWidth;
                    DetectionCanvas.Height = _currentImage.PixelHeight;
                    
                    TxtImageName.Text = $"图片: {_imageName}";
                    TxtImageSize.Text = $"尺寸: {_currentImage.PixelWidth} x {_currentImage.PixelHeight}";
                    TxtStatus.Text = "图片加载成功";
                }
                else
                {
                    MessageBox.Show($"找不到图片文件: {imagePath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
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
            DialogResult = false;
            Close();
        }

        // 参数调整
        private void SldConfidence_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _confidenceThreshold = e.NewValue;
            if (TxtConfidence != null)
            {
                TxtConfidence.Text = e.NewValue.ToString("F2");
            }
        }

        private void SldIOU_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _iouThreshold = e.NewValue;
            if (TxtIOU != null)
            {
                TxtIOU.Text = e.NewValue.ToString("F2");
            }
        }

        // 开始检测
        private async void BtnDetect_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImage == null)
            {
                MessageBox.Show("图片未加载", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                TxtStatus.Text = "检测中...";
                BtnDetect.IsEnabled = false;

                // TODO: 调用实际的检测API
                // var result = await _apiService.DetectImageAsync(_imageName, _confidenceThreshold, _iouThreshold);
                // _detectionResults = result?.Bboxes ?? new List<BoundingBox>();

                // 暂时使用空列表
                _detectionResults = new List<BoundingBox>();

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
                var rect = new System.Windows.Shapes.Rectangle
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

        // 应用结果
        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_detectionResults.Count == 0)
            {
                MessageBox.Show("没有检测结果可应用", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 保存检测结果到标注文件
                var annotation = new AnnotationData
                {
                    ImageName = _imageName,
                    Width = _currentImage?.PixelWidth ?? 0,
                    Height = _currentImage?.PixelHeight ?? 0,
                    Bboxes = _detectionResults
                };

                var success = await _apiService.SaveAnnotationAsync(_imageName, annotation);
                
                if (success)
                {
                    MessageBox.Show("检测结果已应用到标注", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("应用结果失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用结果失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 取消
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
