using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoloAnnotator.Services;

namespace YoloAnnotator
{
    public partial class CameraRealtimeWindow : Window
    {
        private readonly ApiService _apiService;
        private bool _cameraActive = false;
        private bool _detectActive = false;
        private string _currentOperation = string.Empty;
        private DispatcherTimer? _cameraTimer;
        private DateTime _lastFrameTime = DateTime.Now;
        private int _frameCount = 0;

        public CameraRealtimeWindow()
        {
            InitializeComponent();
            _apiService = new ApiService();
            
            // 初始化定时器
            _cameraTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
            };
            _cameraTimer.Tick += CameraTimer_Tick;
        }

        private async void BtnStartCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cameraId = int.Parse((CboCameraId.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "0");
                var resolution = int.Parse((CboResolution.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "640");
                var width = resolution;
                var height = resolution == 640 ? 480 : (resolution == 1280 ? 720 : 1080);

                UpdateStatus("正在启动摄像头...");
                Console.WriteLine($"[DEBUG] 启动摄像头: ID={cameraId}, 分辨率={width}x{height}");
                
                var result = await _apiService.InitCameraAsync(cameraId, width, height);
                Console.WriteLine($"[DEBUG] 摄像头初始化结果: {result?.Success}");

                if (result != null && result.Success)
                {
                    _cameraActive = true;
                    BtnStartCamera.IsEnabled = false;
                    BtnStopCamera.IsEnabled = true;
                    CboCameraId.IsEnabled = false;
                    CboResolution.IsEnabled = false;
                    TxtNoCamera.Visibility = Visibility.Collapsed;
                    TxtDetectHint.Text = "选择检测功能开始";
                    TxtDetectHint.Foreground = new SolidColorBrush(Colors.Gray);
                    
                    UpdateStatus("摄像头已启动");
                    UpdateCameraInfo($"分辨率: {result.Width}×{result.Height}");
                    
                    // 开始获取帧
                    _cameraTimer?.Start();
                    Console.WriteLine("[DEBUG] 摄像头定时器已启动");
                }
                else
                {
                    UpdateStatus("摄像头启动失败！");
                    MessageBox.Show("摄像头启动失败，请检查摄像头是否可用。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 摄像头启动异常: {ex.Message}");
                UpdateStatus($"摄像头启动异常: {ex.Message}");
                MessageBox.Show($"摄像头启动异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnStopCamera_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_detectActive)
                {
                    StopDetection();
                }

                UpdateStatus("正在停止摄像头...");
                var success = await _apiService.StopCameraAsync();

                if (success)
                {
                    _cameraActive = false;
                    _cameraTimer?.Stop();
                    
                    BtnStartCamera.IsEnabled = true;
                    BtnStopCamera.IsEnabled = false;
                    CboCameraId.IsEnabled = true;
                    CboResolution.IsEnabled = true;
                    TxtNoCamera.Visibility = Visibility.Visible;
                    CameraImage.Source = null;
                    
                    UpdateStatus("摄像头已停止");
                    UpdateCameraInfo("");
                    
                    ResetDetectionStats();
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"停止摄像头异常: {ex.Message}");
            }
        }

        private async void BtnFaceDetect_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[DEBUG] 点击人脸检测按钮");
            StartDetection("face_detect", "人脸检测");
        }

        private async void BtnLaneDetect_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[DEBUG] 点击车道线检测按钮");
            StartDetection("lane_detect", "车道线检测");
        }

        private async void BtnHandDetect_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[DEBUG] 点击手势识别按钮");
            StartDetection("hand_detect", "手势识别");
        }

        private void BtnStopDetect_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[DEBUG] 点击停止检测按钮");
            StopDetection();
        }

        private void StartDetection(string operation, string operationName)
        {
            Console.WriteLine($"[DEBUG] 开始检测: {operation}");
            
            if (!_cameraActive)
            {
                Console.WriteLine("[DEBUG] 摄像头未启动，提示用户");
                MessageBox.Show("请先启动摄像头！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _currentOperation = operation;
            _detectActive = true;
            _frameCount = 0;
            
            TxtCurrentOperation.Text = operationName;
            TxtFPS.Text = "0";
            
            BtnFaceDetect.IsEnabled = false;
            BtnLaneDetect.IsEnabled = false;
            BtnHandDetect.IsEnabled = false;
            BtnStopDetect.IsEnabled = true;
            
            UpdateStatus($"开始{operationName}...");
            Console.WriteLine($"[DEBUG] {operationName}已启动");
        }

        private void StopDetection()
        {
            _currentOperation = string.Empty;
            _detectActive = false;
            
            TxtCurrentOperation.Text = "无";
            TxtDetectionCount.Text = "0";
            TxtFPS.Text = "0";
            
            BtnFaceDetect.IsEnabled = true;
            BtnLaneDetect.IsEnabled = true;
            BtnHandDetect.IsEnabled = true;
            BtnStopDetect.IsEnabled = false;
            
            UpdateStatus("检测已停止");
            ResetDetectionStats();
        }

        private void ResetDetectionStats()
        {
            TxtCurrentOperation.Text = "无";
            TxtDetectionCount.Text = "0";
            TxtFPS.Text = "0";
        }

        private async void CameraTimer_Tick(object? sender, EventArgs e)
        {
            if (!_cameraActive)
                return;

            try
            {
                // 计算FPS
                _frameCount++;
                var now = DateTime.Now;
                var elapsed = (now - _lastFrameTime).TotalSeconds;
                if (elapsed >= 1.0)
                {
                    TxtFPS.Text = Math.Round(_frameCount / elapsed, 1).ToString("F1");
                    _frameCount = 0;
                    _lastFrameTime = now;
                }

                // 获取摄像头帧
                var operation = _detectActive ? _currentOperation : "capture";
                var result = await _apiService.GetCameraFrameAsync(operation);

                if (result != null && result.Success && !string.IsNullOrEmpty(result.Image))
                {
                    // 显示摄像头画面
                    var imageBytes = Convert.FromBase64String(result.Image);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(imageBytes);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    Dispatcher.Invoke(() =>
                    {
                        CameraImage.Source = bitmap;
                        
                        // 更新检测统计
                        if (_detectActive)
                        {
                            if (operation == "face_detect")
                            {
                                TxtDetectionCount.Text = result.FaceCount.ToString();
                            }
                            else if (operation == "lane_detect")
                            {
                                TxtDetectionCount.Text = result.LaneCount.ToString();
                            }
                            else if (operation == "hand_detect")
                            {
                                TxtDetectionCount.Text = result.HandCount.ToString();
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"摄像头帧处理失败: {ex.Message}");
            }
        }

        private void UpdateStatus(string message)
        {
            TxtStatus.Text = message;
        }

        private void UpdateCameraInfo(string info)
        {
            TxtCameraInfo.Text = info;
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
            if (_cameraActive)
            {
                BtnStopCamera_Click(sender, e);
            }
            Close();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_cameraActive)
            {
                _ = _apiService.StopCameraAsync();
                _cameraTimer?.Stop();
            }
        }
    }
}