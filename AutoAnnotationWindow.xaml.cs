using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Newtonsoft.Json;
using YoloAnnotator.Models;
using YoloAnnotator.Services;

namespace YoloAnnotator
{
    public partial class AutoAnnotationWindow : Window
    {
        private readonly ApiService _apiService;
        private List<ImageItem> _images = new List<ImageItem>();
        private List<DetectionResult> _currentDetections = new List<DetectionResult>();
        private string _currentImageName = "";
        private BitmapImage? _currentBitmap = null;

        public AutoAnnotationWindow()
        {
            InitializeComponent();
            _apiService = new ApiService();
            LoadImages();
        }

        // 窗口控制按钮事件
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

        private async void LoadImages()
        {
            try
            {
                TxtStatus.Text = "正在加载图片列表...";
                var response = await _apiService.GetImagesAsync();
                if (response != null && response.Count > 0)
                {
                    _images = response.Select(name => new ImageItem 
                    { 
                        Name = name, 
                        Status = "未标注" 
                    }).ToList();
                    
                    LstImages.ItemsSource = _images;
                    TxtStatus.Text = $"已加载 {_images.Count} 张图片";
                }
                else
                {
                    TxtStatus.Text = "未找到图片";
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "加载图片失败";
                MessageBox.Show($"加载图片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LstImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstImages.SelectedItem is ImageItem selectedImage)
            {
                // 清除之前的检测结果
                _currentDetections.Clear();
                LstDetections.ItemsSource = null;
                AnnotationCanvas.Children.Clear();
                TxtDetectionCount.Text = "0 个对象";
                BtnSaveCurrent.IsEnabled = false;
                BtnClearCurrent.IsEnabled = false;
                
                _currentImageName = selectedImage.Name;
                await LoadImagePreview(_currentImageName);
            }
        }

        private async Task LoadImagePreview(string imageName)
        {
            try
            {
                TxtStatus.Text = $"正在加载图片: {imageName}";
                
                var base64Image = await _apiService.GetImageBase64Async(imageName);
                if (string.IsNullOrEmpty(base64Image))
                {
                    TxtStatus.Text = "加载图片失败";
                    return;
                }

                var imageBytes = Convert.FromBase64String(base64Image);
                using (var stream = new MemoryStream(imageBytes))
                {
                    _currentBitmap = new BitmapImage();
                    _currentBitmap.BeginInit();
                    _currentBitmap.StreamSource = stream;
                    _currentBitmap.CacheOption = BitmapCacheOption.OnLoad;
                    _currentBitmap.EndInit();
                    _currentBitmap.Freeze();

                    ImgPreview.Source = _currentBitmap;
                    AnnotationCanvas.Width = _currentBitmap.PixelWidth;
                    AnnotationCanvas.Height = _currentBitmap.PixelHeight;
                    
                    TxtNoImage.Visibility = Visibility.Collapsed;
                }

                TxtStatus.Text = $"已加载: {imageName}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "加载图片失败";
                MessageBox.Show($"加载图片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAutoAnnotate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImageName))
            {
                MessageBox.Show("请先选择一张图片", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                BtnAutoAnnotate.IsEnabled = false;
                TxtStatus.Text = "正在进行自动标注...";
                
                // 获取参数
                var taskType = GetTaskType();
                var modelSize = GetModelSize();
                var confidence = (float)SliderConfidence.Value;
                var iou = (float)SliderIOU.Value;

                // 调用API
                var request = new
                {
                    image_name = _currentImageName,
                    task_type = taskType,
                    model_size = modelSize,
                    conf_threshold = confidence,
                    iou_threshold = iou
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                using var httpClient = new HttpClient();
                var response = await httpClient.PostAsync("http://localhost:8000/api/pretrained/annotate", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<AutoAnnotationResponse>(resultJson);
                    
                    if (result != null && result.Detections != null)
                    {
                        _currentDetections = result.Detections.Select(d => new DetectionResult
                        {
                            ClassName = d.ClassName ?? "未知",
                            Confidence = d.Confidence,
                            Type = d.AnnotationType ?? taskType,
                            BoundingBox = d
                        }).ToList();
                        
                        LstDetections.ItemsSource = _currentDetections;
                        TxtDetectionCount.Text = $"{_currentDetections.Count} 个对象";
                        
                        // 绘制检测结果
                        DrawDetections();
                        
                        // 更新图片状态
                        var imageItem = _images.FirstOrDefault(img => img.Name == _currentImageName);
                        if (imageItem != null)
                        {
                            imageItem.Status = $"检测到 {_currentDetections.Count} 个";
                        }
                        
                        BtnSaveCurrent.IsEnabled = true;
                        BtnClearCurrent.IsEnabled = true;
                        
                        TxtStatus.Text = $"检测完成，找到 {_currentDetections.Count} 个对象";
                    }
                    else
                    {
                        TxtStatus.Text = "未检测到对象";
                        MessageBox.Show("未检测到任何对象", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    TxtStatus.Text = "自动标注失败";
                    MessageBox.Show($"自动标注失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "自动标注失败";
                MessageBox.Show($"自动标注失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnAutoAnnotate.IsEnabled = true;
            }
        }

        private async void BtnBatchAnnotate_Click(object sender, RoutedEventArgs e)
        {
            if (_images.Count == 0)
            {
                MessageBox.Show("没有可标注的图片", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"确定要对所有 {_images.Count} 张图片进行批量自动标注吗？\n这可能需要一些时间。", 
                "确认", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes) return;

            try
            {
                BtnBatchAnnotate.IsEnabled = false;
                BtnAutoAnnotate.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.Value = 0;
                
                var taskType = GetTaskType();
                var modelSize = GetModelSize();
                var confidence = (float)SliderConfidence.Value;
                var iou = (float)SliderIOU.Value;

                var request = new
                {
                    image_names = _images.Select(img => img.Name).ToList(),
                    task_type = taskType,
                    model_size = modelSize,
                    conf_threshold = confidence,
                    iou_threshold = iou,
                    save_annotations = true
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10); // 增加超时时间
                
                TxtStatus.Text = "正在批量标注...";
                var response = await httpClient.PostAsync("http://localhost:8000/api/pretrained/batch-annotate", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var resultJson = await response.Content.ReadAsStringAsync();
                    var batchResult = JsonConvert.DeserializeObject<BatchAnnotationResponse>(resultJson);
                    
                    if (batchResult != null)
                    {
                        ProgressBar.Value = 100;
                        
                        // 更新图片状态
                        foreach (var imgResult in batchResult.Results ?? new List<ImageAnnotationResult>())
                        {
                            var imageItem = _images.FirstOrDefault(img => img.Name == imgResult.ImageName);
                            if (imageItem != null)
                            {
                                imageItem.Status = imgResult.Success 
                                    ? $"✓ {imgResult.DetectionCount} 个" 
                                    : "✗ 失败";
                            }
                        }
                        
                        LstImages.Items.Refresh();
                        
                        TxtStatus.Text = $"批量标注完成: 成功 {batchResult.SuccessCount}/{batchResult.TotalCount}";
                        MessageBox.Show(
                            $"批量标注完成！\n\n成功: {batchResult.SuccessCount}\n失败: {batchResult.FailedCount}\n总计: {batchResult.TotalCount}", 
                            "完成", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    TxtStatus.Text = "批量标注失败";
                    MessageBox.Show($"批量标注失败: {error}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "批量标注失败";
                MessageBox.Show($"批量标注失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnBatchAnnotate.IsEnabled = true;
                BtnAutoAnnotate.IsEnabled = true;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnSaveCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDetections.Count == 0 || string.IsNullOrEmpty(_currentImageName))
            {
                MessageBox.Show("没有可保存的标注", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                TxtStatus.Text = "正在保存标注...";
                
                var annotation = new AnnotationData
                {
                    ImageName = _currentImageName,
                    Width = _currentBitmap?.PixelWidth ?? 0,
                    Height = _currentBitmap?.PixelHeight ?? 0,
                    Bboxes = _currentDetections.Select(d => d.BoundingBox).ToList()
                };

                var success = await _apiService.SaveAnnotationAsync(_currentImageName, annotation);
                
                if (success)
                {
                    var imageItem = _images.FirstOrDefault(img => img.Name == _currentImageName);
                    if (imageItem != null)
                    {
                        imageItem.Status = "✓ 已保存";
                    }
                    LstImages.Items.Refresh();
                    
                    TxtStatus.Text = "标注已保存";
                    MessageBox.Show("标注保存成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TxtStatus.Text = "保存失败";
                    MessageBox.Show("保存标注失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "保存失败";
                MessageBox.Show($"保存标注失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearCurrent_Click(object sender, RoutedEventArgs e)
        {
            _currentDetections.Clear();
            LstDetections.ItemsSource = null;
            AnnotationCanvas.Children.Clear();
            TxtDetectionCount.Text = "0 个对象";
            BtnSaveCurrent.IsEnabled = false;
            BtnClearCurrent.IsEnabled = false;
            TxtStatus.Text = "已清除检测结果";
        }

        private void DrawDetections()
        {
            if (_currentBitmap == null) return;
            
            AnnotationCanvas.Children.Clear();
            
            var imgWidth = _currentBitmap.PixelWidth;
            var imgHeight = _currentBitmap.PixelHeight;

            foreach (var detection in _currentDetections)
            {
                var bbox = detection.BoundingBox;
                
                // 转换归一化坐标到像素坐标
                var x = bbox.X * imgWidth;
                var y = bbox.Y * imgHeight;
                var width = bbox.Width * imgWidth;
                var height = bbox.Height * imgHeight;
                
                // 计算左上角坐标
                var left = x - width / 2;
                var top = y - height / 2;

                // 根据标注类型绘制不同的可视化
                if (bbox.AnnotationType == "keypoints" && bbox.Keypoints != null && bbox.Keypoints.Count > 0)
                {
                    // 姿态估计 - 绘制关键点和骨架
                    DrawPoseKeypoints(bbox, imgWidth, imgHeight);
                }
                else if (bbox.AnnotationType == "polygon" && bbox.Points != null && bbox.Points.Count > 0)
                {
                    // 实例分割 - 绘制多边形
                    DrawPolygon(bbox, imgWidth, imgHeight);
                }
                else
                {
                    // 目标检测/旋转框 - 绘制边界框
                    var rect = new Rectangle
                    {
                        Width = width,
                        Height = height,
                        Stroke = Brushes.Lime,
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 0))
                    };
                    
                    Canvas.SetLeft(rect, left);
                    Canvas.SetTop(rect, top);
                    AnnotationCanvas.Children.Add(rect);
                }

                // 绘制标签
                var label = new TextBlock
                {
                    Text = $"{bbox.ClassName} {bbox.Confidence:P0}",
                    Background = Brushes.Lime,
                    Foreground = Brushes.Black,
                    Padding = new Thickness(4, 2, 4, 2),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                };
                
                Canvas.SetLeft(label, left);
                Canvas.SetTop(label, Math.Max(0, top - 20));
                AnnotationCanvas.Children.Add(label);
            }
        }

        private void DrawPoseKeypoints(BoundingBox bbox, int imgWidth, int imgHeight)
        {
            if (bbox.Keypoints == null || bbox.Keypoints.Count == 0) return;

            // COCO 姿态估计的 17 个关键点连接关系（骨架）
            var skeleton = new List<(int, int)>
            {
                (0, 1), (0, 2), (1, 3), (2, 4),  // 头部
                (5, 6), (5, 7), (7, 9), (6, 8), (8, 10),  // 上半身
                (5, 11), (6, 12), (11, 12),  // 躯干
                (11, 13), (13, 15), (12, 14), (14, 16)  // 下半身
            };

            // 绘制骨架连接线
            foreach (var (start, end) in skeleton)
            {
                if (start < bbox.Keypoints.Count && end < bbox.Keypoints.Count)
                {
                    var startPoint = bbox.Keypoints[start];
                    var endPoint = bbox.Keypoints[end];
                    
                    // 检查关键点是否有效（坐标不为0）
                    if (startPoint.Count >= 2 && endPoint.Count >= 2 &&
                        (startPoint[0] > 0 || startPoint[1] > 0) &&
                        (endPoint[0] > 0 || endPoint[1] > 0))
                    {
                        var line = new Line
                        {
                            X1 = startPoint[0] * imgWidth,
                            Y1 = startPoint[1] * imgHeight,
                            X2 = endPoint[0] * imgWidth,
                            Y2 = endPoint[1] * imgHeight,
                            Stroke = Brushes.Cyan,
                            StrokeThickness = 2
                        };
                        AnnotationCanvas.Children.Add(line);
                    }
                }
            }

            // 绘制关键点
            for (int i = 0; i < bbox.Keypoints.Count; i++)
            {
                var kpt = bbox.Keypoints[i];
                if (kpt.Count >= 2 && (kpt[0] > 0 || kpt[1] > 0))
                {
                    var circle = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = Brushes.Red,
                        Stroke = Brushes.White,
                        StrokeThickness = 1
                    };
                    
                    var px = kpt[0] * imgWidth;
                    var py = kpt[1] * imgHeight;
                    
                    Canvas.SetLeft(circle, px - 4);
                    Canvas.SetTop(circle, py - 4);
                    AnnotationCanvas.Children.Add(circle);
                }
            }
        }

        private void DrawPolygon(BoundingBox bbox, int imgWidth, int imgHeight)
        {
            if (bbox.Points == null || bbox.Points.Count < 3) return;

            var points = new PointCollection();
            foreach (var pt in bbox.Points)
            {
                if (pt.Count >= 2)
                {
                    points.Add(new Point(pt[0] * imgWidth, pt[1] * imgHeight));
                }
            }

            if (points.Count >= 3)
            {
                var polygon = new Polygon
                {
                    Points = points,
                    Stroke = Brushes.Yellow,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 0))
                };
                AnnotationCanvas.Children.Add(polygon);
            }
        }

        private string GetTaskType()
        {
            return CmbTaskType.SelectedIndex switch
            {
                0 => "detection",
                1 => "segmentation",
                2 => "pose",
                3 => "obb",
                _ => "detection"
            };
        }

        private string GetModelSize()
        {
            return CmbModel.SelectedIndex switch
            {
                0 => "n",
                1 => "s",
                2 => "m",
                3 => "l",
                4 => "x",
                _ => "n"
            };
        }
    }

    public class ImageItem
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class DetectionResult
    {
        public string ClassName { get; set; } = "";
        public double Confidence { get; set; }
        public string Type { get; set; } = "";
        public BoundingBox BoundingBox { get; set; } = new BoundingBox();
    }

    public class AutoAnnotationResponse
    {
        [JsonProperty("image_name")]
        public string? ImageName { get; set; }
        
        [JsonProperty("detections")]
        public List<BoundingBox>? Detections { get; set; }
        
        [JsonProperty("count")]
        public int Count { get; set; }
    }

    public class BatchAnnotationResponse
    {
        [JsonProperty("total_count")]
        public int TotalCount { get; set; }
        
        [JsonProperty("success_count")]
        public int SuccessCount { get; set; }
        
        [JsonProperty("failed_count")]
        public int FailedCount { get; set; }
        
        [JsonProperty("results")]
        public List<ImageAnnotationResult>? Results { get; set; }
    }

    public class ImageAnnotationResult
    {
        [JsonProperty("image_name")]
        public string ImageName { get; set; } = "";
        
        [JsonProperty("success")]
        public bool Success { get; set; }
        
        [JsonProperty("detection_count")]
        public int DetectionCount { get; set; }
        
        [JsonProperty("error")]
        public string? Error { get; set; }
    }
}
