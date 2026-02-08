using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Net.Http;
using System.Text.Json;

namespace YoloAnnotator
{
    public partial class VideoDetectionWindow : Window
    {
        // Windows API for proper window maximization
        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        // 处理WM_GETMINMAXINFO以正确处理窗口最大化
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;

                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    MONITORINFO monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    GetMonitorInfo(monitor, ref monitorInfo);

                    RECT rcWorkArea = monitorInfo.rcWork;
                    RECT rcMonitorArea = monitorInfo.rcMonitor;

                    mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                    mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                    mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
                }

                Marshal.StructureToPtr(mmi, lParam, true);
                handled = true;
            }

            return IntPtr.Zero;
        }

        private HttpClient _httpClient;
        private bool _isDetecting = false;
        private bool _isPaused = false;
        private Task? _detectionTask;
        private System.Threading.CancellationTokenSource? _cancellationTokenSource;
        private double _videoWidth = 0;
        private double _videoHeight = 0;
        private DateTime _lastFrameTime;
        private int _frameCount = 0;

        private class DetectionResult
        {
            public string DisplayText { get; set; } = "";
            public string ClassName { get; set; } = "";
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public int ClassId { get; set; }
            public double Confidence { get; set; }
        }

        public VideoDetectionWindow()
        {
            InitializeComponent();

            // 设置窗口状态变化处理
            this.StateChanged += Window_StateChanged;

            // 初始化源以处理WM_GETMINMAXINFO
            this.SourceInitialized += (s, e) =>
            {
                HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
                source.AddHook(WndProc);
            };

            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("http://localhost:8000");
            
            // 置信度滑块值变化
            SldConfidence.ValueChanged += (s, e) =>
            {
                TxtConfidence.Text = SldConfidence.Value.ToString("F2");
            };
            
            // 使用Loaded事件来初始化，避免XAML加载时触发SelectionChanged
            this.Loaded += (s, e) =>
            {
                // 确保控件已初始化后，手动设置默认选择
                if (CboSourceType.SelectedItem == null)
                {
                    CboSourceType.SelectedIndex = 0;
                }
            };
        }

        private void CboSourceType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 检查控件是否已初始化
            if (CboSourceType.SelectedItem == null || 
                PanelFileSource == null || 
                PanelRtspSource == null ||
                CboVideoSource == null ||
                BtnSelectVideo == null)
                return;
            
            var selectedType = (CboSourceType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "file";
            
            // 根据类型显示/隐藏不同的输入控件
            try
            {
                switch (selectedType)
                {
                    case "file":
                        PanelFileSource.Visibility = Visibility.Visible;
                        PanelRtspSource.Visibility = Visibility.Collapsed;
                        CboVideoSource.IsEnabled = true;
                        BtnSelectVideo.IsEnabled = true;
                        break;
                    case "camera":
                        PanelFileSource.Visibility = Visibility.Collapsed;
                        PanelRtspSource.Visibility = Visibility.Collapsed;
                        break;
                    case "rtsp":
                        PanelFileSource.Visibility = Visibility.Collapsed;
                        PanelRtspSource.Visibility = Visibility.Visible;
                        break;
                }
            }
            catch
            {
                // 忽略初始化过程中的异常
            }
        }

        private void BtnSelectVideo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.mov;*.mkv;*.flv;*.wmv;*.webm|所有文件|*.*",
                Title = "选择视频文件"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                CboVideoSource.Items.Clear();
                CboVideoSource.Items.Add(new ComboBoxItem { Content = openFileDialog.FileName, IsSelected = true });
                CboVideoSource.Text = openFileDialog.FileName;
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var sourceType = (CboSourceType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "file";
            string videoSource = "";
            
            // 获取视频源
            switch (sourceType)
            {
                case "file":
                    videoSource = CboVideoSource.Text.Trim();
                    if (string.IsNullOrEmpty(videoSource))
                    {
                        MessageBox.Show("请选择视频文件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!File.Exists(videoSource))
                    {
                        MessageBox.Show("视频文件不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    break;
                    
                case "camera":
                    videoSource = "0"; // 默认摄像头索引
                    break;
                    
                case "rtsp":
                    videoSource = TxtRtspUrl.Text.Trim();
                    if (string.IsNullOrEmpty(videoSource) || videoSource == "rtsp://")
                    {
                        MessageBox.Show("请输入RTSP流地址", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (!videoSource.StartsWith("rtsp://"))
                    {
                        MessageBox.Show("RTSP地址必须以 rtsp:// 开头", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    break;
            }

            try
            {
                // 更新UI状态
                BtnStart.IsEnabled = false;
                BtnStop.IsEnabled = true;
                BtnPause.IsEnabled = true;
                BtnResume.IsEnabled = false;
                CboSourceType.IsEnabled = false;
                CboVideoSource.IsEnabled = false;
                BtnSelectVideo.IsEnabled = false;
                TxtStatus.Text = "正在启动视频检测...";

                Console.WriteLine($"[VIDEO-UI] 启动检测: type={sourceType}, source={videoSource}");

                // 启动视频检测
                var request = new
                {
                    source = videoSource,
                    source_type = sourceType,
                    model_type = "yolov8n",
                    confidence_threshold = SldConfidence.Value
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync("/api/video/detect/start", content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"启动失败: {responseText}");
                }

                var result = JsonSerializer.Deserialize<JsonElement>(responseText);
                bool success = result.GetProperty("success").GetBoolean();

                if (!success)
                {
                    throw new Exception(result.GetProperty("message").GetString());
                }

                // 获取视频信息
                _videoWidth = 0;
                _videoHeight = 0;
                TxtFrameInfo.Text = "";
                
                if (result.TryGetProperty("video_info", out var videoInfo))
                {
                    if (videoInfo.TryGetProperty("width", out var width))
                        _videoWidth = width.GetInt32();
                    if (videoInfo.TryGetProperty("height", out var height))
                        _videoHeight = height.GetInt32();
                    if (videoInfo.TryGetProperty("frame_count", out var frameCount))
                        TxtFrameInfo.Text = $"总帧数: {frameCount.GetInt32()}";
                    
                    // RTSP/摄像头显示实时信息
                    if (videoInfo.TryGetProperty("source_type", out var srcType))
                    {
                        var typeStr = srcType.GetString();
                        if (typeStr == "camera" || typeStr == "rtsp")
                        {
                            TxtFrameInfo.Text = $"实时流 ({typeStr})";
                        }
                    }
                }

                // 开始检测循环
                _isDetecting = true;
                _isPaused = false;
                _cancellationTokenSource = new System.Threading.CancellationTokenSource();
                _detectionTask = Task.Run(() => DetectionLoop(_cancellationTokenSource.Token));

                TxtStatus.Text = "视频检测进行中...";
                _lastFrameTime = DateTime.Now;
                _frameCount = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动视频检测失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // 恢复UI状态
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                BtnPause.IsEnabled = false;
                BtnResume.IsEnabled = false;
                CboSourceType.IsEnabled = true;
                CboVideoSource.IsEnabled = true;
                BtnSelectVideo.IsEnabled = true;
                TxtStatus.Text = "启动失败";
            }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 停止检测
                _isDetecting = false;
                _cancellationTokenSource?.Cancel();

                // 调用后端停止
                await _httpClient.PostAsync("/api/video/detect/stop", null);

                // 恢复UI状态
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                BtnPause.IsEnabled = false;
                BtnResume.IsEnabled = false;
                CboSourceType.IsEnabled = true;
                CboVideoSource.IsEnabled = true;
                BtnSelectVideo.IsEnabled = true;
                
                // 隐藏RTSP输入框时也要恢复
                var sourceType = (CboSourceType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "file";
                if (sourceType == "rtsp")
                {
                    PanelRtspSource.Visibility = Visibility.Collapsed;
                }
                
                TxtStatus.Text = "视频检测已停止";
                TxtFPS.Text = "";
                LstDetections.Items.Clear();
                DetectionCanvas.Children.Clear();
                VideoImage.Source = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止视频检测失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = true;
            BtnPause.IsEnabled = false;
            BtnResume.IsEnabled = true;
            TxtStatus.Text = "视频检测已暂停";
        }

        private void BtnResume_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = false;
            BtnPause.IsEnabled = true;
            BtnResume.IsEnabled = false;
            TxtStatus.Text = "视频检测进行中...";
        }

        private async Task DetectionLoop(System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine("[VIDEO-UI] 检测循环启动");
                
                while (_isDetecting && !cancellationToken.IsCancellationRequested)
                {
                    if (_isPaused)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    try
                    {
                        // 获取置信度值（在UI线程获取，避免线程问题）
                        float confidence = await Dispatcher.InvokeAsync(() => (float)SldConfidence.Value);
                        
                        // 获取视频帧
                        string url = $"/api/video/detect/frame?confidence_threshold={confidence}";
                        Console.WriteLine($"[VIDEO-UI] 请求帧: {url}");
                        
                        var response = await _httpClient.GetAsync(url, cancellationToken);
                        Console.WriteLine($"[VIDEO-UI] 响应状态: {response.StatusCode}");

                        if (!response.IsSuccessStatusCode)
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"[VIDEO-UI] 请求失败: {errorContent}");
                            break;
                        }

                        var responseText = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<JsonElement>(responseText);

                        if (!result.GetProperty("success").GetBoolean())
                        {
                            string message = result.TryGetProperty("message", out var msg)
                                ? msg.GetString()!
                                : "获取帧失败";

                            Console.WriteLine($"[VIDEO-UI] 获取帧失败: {message}");
                            
                            if (message.Contains("视频结束"))
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    BtnStop_Click(null!, null!);
                                });
                                break;
                            }
                            continue;
                        }

                        // 解析帧和检测结果
                        string frameBase64 = result.GetProperty("frame").GetString()!;
                        var detections = new List<DetectionResult>();
                        
                        if (result.TryGetProperty("detections", out var detsElement))
                        {
                            foreach (var d in detsElement.EnumerateArray())
                            {
                                try
                                                                    {
                                                                    int classId = d.GetProperty("class_id").GetInt32();
                                                                    string className = "";
                                                                    if (d.TryGetProperty("class_name", out var nameElement))
                                                                    {
                                                                        className = nameElement.GetString() ?? "";
                                                                    }
                                                                    double conf = d.GetProperty("confidence").GetDouble();
                                
                                                                    // 如果有类别名称，优先显示类别名称；否则显示 "Class X"
                                                                    string displayText = !string.IsNullOrEmpty(className)
                                                                        ? $"{className} ({conf:F2})"
                                                                        : $"Class {classId} ({conf:F2})";
                                
                                                                    detections.Add(new DetectionResult
                                                                    {
                                                                        X = d.GetProperty("x").GetDouble(),
                                                                        Y = d.GetProperty("y").GetDouble(),
                                                                        Width = d.GetProperty("width").GetDouble(),
                                                                        Height = d.GetProperty("height").GetDouble(),
                                                                        ClassId = classId,
                                                                        Confidence = conf,
                                                                        DisplayText = displayText,
                                                                        ClassName = className
                                                                    });
                                                                }
                                                                catch { }                            }
                        }
                        
                        Console.WriteLine($"[VIDEO-UI] 检测到 {detections.Count} 个目标，帧大小: {frameBase64.Length} 字符");

                        // 在UI线程更新显示
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                // 显示视频帧
                                var imageBytes = Convert.FromBase64String(frameBase64);
                                using var stream = new MemoryStream(imageBytes);
                                var bitmapImage = new BitmapImage();
                                bitmapImage.BeginInit();
                                bitmapImage.StreamSource = stream;
                                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                bitmapImage.EndInit();
                                bitmapImage.Freeze();
                                VideoImage.Source = bitmapImage;
                                
                                Console.WriteLine($"[VIDEO-UI] 视频帧显示成功: {bitmapImage.PixelWidth}x{bitmapImage.PixelHeight}");

                                // 更新画布大小
                                if (bitmapImage.PixelWidth > 0 && bitmapImage.PixelHeight > 0)
                                {
                                    VideoContainer.Width = bitmapImage.PixelWidth;
                                    VideoContainer.Height = bitmapImage.PixelHeight;
                                    DetectionCanvas.Width = bitmapImage.PixelWidth;
                                    DetectionCanvas.Height = bitmapImage.PixelHeight;
                                }

                                // 绘制检测框
                                DrawDetections(detections, bitmapImage.PixelWidth, bitmapImage.PixelHeight);

                                // 更新检测结果列表
                                LstDetections.Items.Clear();
                                foreach (var det in detections)
                                {
                                    LstDetections.Items.Add(det);
                                }

                                // 更新FPS
                                _frameCount++;
                                var elapsed = DateTime.Now - _lastFrameTime;
                                if (elapsed.TotalSeconds >= 1)
                                {
                                    double fps = _frameCount / elapsed.TotalSeconds;
                                    TxtFPS.Text = $"FPS: {fps:F1}";
                                    _frameCount = 0;
                                    _lastFrameTime = DateTime.Now;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[VIDEO-UI] UI更新错误: {ex.Message}");
                            }
                        });
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VIDEO-UI] 检测循环错误: {ex.Message}");
                        await Task.Delay(100, cancellationToken);
                    }
                }
                
                Console.WriteLine("[VIDEO-UI] 检测循环结束");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[VIDEO-UI] 任务被取消");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VIDEO-UI] 异常: {ex.Message}");
            }
        }

        private void DrawDetections(List<DetectionResult> detections, double imageWidth, double imageHeight)
        {
            DetectionCanvas.Children.Clear();

            foreach (var det in detections)
            {
                // 转换归一化坐标到像素坐标
                double x = (det.X - det.Width / 2) * imageWidth;
                double y = (det.Y - det.Height / 2) * imageHeight;
                double width = det.Width * imageWidth;
                double height = det.Height * imageHeight;

                // 绘制检测框
                var rect = new Rectangle
                {
                    Stroke = Brushes.LimeGreen,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent,
                    Width = width,
                    Height = height
                };
                
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                DetectionCanvas.Children.Add(rect);

                // 绘制标签
                var label = new TextBlock
                {
                    Text = det.DisplayText,
                    Foreground = Brushes.LimeGreen,
                    Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    Padding = new Thickness(4, 2, 4, 2),
                    FontSize = 11
                };

                Canvas.SetLeft(label, x);
                Canvas.SetTop(label, y - 20);
                DetectionCanvas.Children.Add(label);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 停止检测
            if (_isDetecting)
            {
                BtnStop_Click(null!, null!);
            }

            _cancellationTokenSource?.Cancel();
            _httpClient?.Dispose();

            base.OnClosed(e);
        }

        // 自定义标题栏事件处理
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 窗口大小改变时的处理
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            RefreshMaximizeRestoreButton();
        }

        private void RefreshMaximizeRestoreButton()
        {
            if (WindowState == WindowState.Maximized)
            {
                BtnMaximize.Visibility = Visibility.Collapsed;
                BtnRestore.Visibility = Visibility.Visible;
            }
            else
            {
                BtnMaximize.Visibility = Visibility.Visible;
                BtnRestore.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}