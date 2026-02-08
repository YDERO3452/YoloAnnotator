using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using YoloAnnotator.Models;
using YoloAnnotator.Services;

namespace YoloAnnotator
{
    public partial class MainWindow : Window
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
        private readonly ApiService _apiService;
        private List<string> _imageNames = new List<string>();
        private string _currentImageName = string.Empty;
        private AnnotationData _currentAnnotation = new AnnotationData();
        private List<string> _classes = new List<string>();
        private bool _isDrawing = false;
        private Point _startPoint;
        private Rectangle? _currentRect;
        private AnnotationTool _currentTool = AnnotationTool.Rectangle;
        private double _zoomLevel = 1.0;
        private BoundingBox? _selectedBbox = null;

        // 项目根目录
        private readonly string _projectRootDir;

        // 多边形标注相关
        private List<Point> _polygonPoints = new List<Point>();
        private List<Line> _polygonLines = new List<Line>();
        private List<Ellipse> _polygonVertices = new List<Ellipse>();

        // 关键点标注相关
        private List<Point> _keypoints = new List<Point>();
        private List<Ellipse> _keypointMarkers = new List<Ellipse>();
        private List<Line> _keypointConnections = new List<Line>();

        // 旋转框标注相关
        private Point _rotateRectCenter;
        private Point _rotateRectStart;
        private Rectangle? _rotateRectPreview;
        private Line? _rotateRectDirection;

        // 圆形标注相关
        private Ellipse? _currentCircle;

        // 线段标注相关
        private Line? _currentLine;

        // 边缘检测辅助工具
        private bool _edgeAssistEnabled = false;
        private Image? _edgeOverlayImage;
        private double _rotateRectAngle = 0;
        private double _rotateRectWidth = 0;
        private double _rotateRectHeight = 0;

        // 撤销栈
        private Stack<List<BoundingBox>> _undoStack = new Stack<List<BoundingBox>>();
        private const int MaxUndoSteps = 50;

        // 拖动相关
        private Point _panStartPos;
        private Point _panOffset = new Point(0, 0);
        private bool _isPanning = false;

        // 复制粘贴相关
        private BoundingBox? _copiedBbox = null;

        // 多选相关
        private readonly List<BoundingBox> _selectedBboxes = new List<BoundingBox>();

        // 标注框调整相关
        private bool _isResizing = false;
        private int _resizeHandleIndex = -1;
        private Point _resizeStartPos = new Point(0, 0);
        private double _originalWidth = 0;
        private double _originalHeight = 0;
        private Point _originalCenter = new Point(0, 0);

        // 类别到颜色的映射
        private readonly Dictionary<string, string> _classColorMap = new Dictionary<string, string>();
        private readonly string[] _defaultColors = new[]
        {
            "#ef4444", "#22c55e", "#3b82f6", "#f59e0b", "#8b5cf6",
            "#ec4899", "#06b6d4", "#84cc16", "#f97316", "#14b8a6",
            "#a855f7", "#eab308", "#6366f1", "#10b981", "#f43f5e",
            "#8b5a2b", "#2dd4bf", "#a3e635", "#fb923c", "#f472b6"
        };

        private string GetClassColor(string className)
        {
            if (string.IsNullOrEmpty(className)) return "#ef4444";
            
            if (_classColorMap.ContainsKey(className))
            {
                return _classColorMap[className];
            }
            
            // 基于类别名称的哈希值分配颜色，确保相同类别名称始终有相同颜色
            var hash = className.GetHashCode();
            var colorIndex = Math.Abs(hash) % _defaultColors.Length;
            var color = _defaultColors[colorIndex];
            
            _classColorMap[className] = color;
            
            // 保存颜色映射到文件
            SaveColorMap();
            
            return color;
        }

        private void LoadColorMap()
        {
            try
            {
                var colorMapPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "color_map.json");
                if (System.IO.File.Exists(colorMapPath))
                {
                    var json = System.IO.File.ReadAllText(colorMapPath);
                    var savedMap = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (savedMap != null)
                    {
                        foreach (var kvp in savedMap)
                        {
                            _classColorMap[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch
            {
                // 加载失败时使用默认颜色
            }
        }

        private void SaveColorMap()
        {
            try
            {
                var colorMapPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "color_map.json");
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_classColorMap, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(colorMapPath, json);
            }
            catch
            {
                // 保存失败时不影响功能
            }
        }

        public enum AnnotationTool
                  {
                      Rectangle,
                      Polygon,
                      Keypoints,
                      RotateRect,
                      Circle,
                      Line,
                      Point,
                      Pan,
                      Eraser,
                      EdgeAssist
                  }
        public MainWindow()
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

            _apiService = new ApiService();

            // 获取项目根目录（从当前输出目录向上两级）
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var projectDir = new DirectoryInfo(baseDir);
            if (projectDir.Name == "net10.0-windows" && projectDir.Parent?.Name == "Debug")
            {
                _projectRootDir = projectDir.Parent.Parent?.FullName ?? baseDir;
            }
            else if (projectDir.Name == "net10.0-windows" && projectDir.Parent?.Name == "Release")
            {
                _projectRootDir = projectDir.Parent.Parent?.FullName ?? baseDir;
            }
            else
            {
                _projectRootDir = baseDir;
            }

            LoadClasses();
            LoadColorMap();
            UpdateToolButtons();

            // 添加键盘事件处理
            this.PreviewKeyDown += Window_PreviewKeyDown;

            // 启动时先检查后端是否可用
            _ = CheckBackendAndLoadImages();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 窗口大小改变时，确保布局自适应
            // 这里的主要逻辑由WPF的Grid和ColumnDefinitions自动处理
            // 我们只需要确保画布区域正确更新
            if (CurrentImage != null)
            {
                // 重新绘制标注框，确保它们在新的窗口尺寸下正确显示
                DrawBoundingBoxes();
            }
        }

        // 自定义标题栏事件处理
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

        private async Task CheckBackendAndLoadImages()
        {
            UpdateStatus("正在检查后端连接...");

            // 尝试多次连接后端
            int maxRetries = 5;
            int retryDelay = 2000; // 2秒

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var isConnected = await _apiService.CheckBackendAsync();
                    if (isConnected)
                    {
                        UpdateStatus("后端连接成功，正在加载图片列表...");
                        
                        // 检测GPU
                        _ = CheckGPUAsync();
                        
                        await LoadImagesAsync();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"后端连接失败 (尝试 {i + 1}/{maxRetries}): {ex.Message}");
                }

                UpdateStatus($"正在等待后端启动... ({i + 1}/{maxRetries})");
                await Task.Delay(retryDelay);
            }

            UpdateStatus("后端服务未就绪，请检查后端是否已启动");
            // 不再弹出警告框，允许用户直接使用应用
        }

        private async Task CheckGPUAsync()
        {
            try
            {
                var cudaInfo = await _apiService.GetCudaInfoAsync();
                if (cudaInfo != null && cudaInfo.Available && cudaInfo.DeviceName != null)
                {
                    var gpuMemory = cudaInfo.GpuMemoryGb.HasValue ? $" ({cudaInfo.GpuMemoryGb:F1} GB)" : "";
                    var cudaVersion = !string.IsNullOrEmpty(cudaInfo.CudaVersion) ? $" CUDA {cudaInfo.CudaVersion}" : "";
                    var driverVersion = !string.IsNullOrEmpty(cudaInfo.DriverVersion) ? $" Driver {cudaInfo.DriverVersion}" : "";
                    StatusBarGPU.Text = $"GPU: {cudaInfo.DeviceName}{gpuMemory}{cudaVersion}{driverVersion}";
                    UpdateStatus($"检测到GPU: {cudaInfo.DeviceName} | PyTorch {cudaInfo.PyTorchVersion}");
                }
                else
                {
                    var reason = cudaInfo?.Reason ?? "请检查CUDA环境";
                    StatusBarGPU.Text = "GPU: 未检测到CUDA";
                    
                    // 显示详细的诊断信息
                    var pytorchVersion = cudaInfo?.PyTorchVersion ?? "Unknown";
                    var cudaVersion = cudaInfo?.CudaVersion ?? "N/A";
                    var system = cudaInfo?.System ?? "Unknown";
                    var pythonVersion = cudaInfo?.PythonVersion ?? "Unknown";
                    
                    var statusMessage = $"GPU未检测 (PyTorch {pytorchVersion}, CUDA {cudaVersion}, {system}, Python {pythonVersion})";
                    if (!string.IsNullOrEmpty(reason))
                    {
                        statusMessage += $" | {reason}";
                    }
                    UpdateStatus(statusMessage);
                }
            }
            catch (Exception ex)
            {
                StatusBarGPU.Text = "GPU: 检测失败";
                Console.WriteLine($"GPU检测失败: {ex.Message}");
            }
        }

        private void LoadClasses()
        {
            try
            {
                var classesPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classes.json");
                if (System.IO.File.Exists(classesPath))
                {
                    var json = System.IO.File.ReadAllText(classesPath);
                    _classes = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
                }
            }
            catch
            {
                _classes = new List<string>();
            }
        }

        private void SaveClasses()
        {
            try
            {
                var classesPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classes.json");
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_classes, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(classesPath, json);
            }
            catch
            {
                MessageBox.Show("保存类别失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateClassesFromAnnotations()
        {
            try
            {
                // 收集所有标注中的类别名称
                var allClassNames = new HashSet<string>();

                // 从当前标注中提取类别
                foreach (var bbox in _currentAnnotation.Bboxes)
                {
                    if (!string.IsNullOrEmpty(bbox.ClassName))
                    {
                        allClassNames.Add(bbox.ClassName);
                    }
                }

                // 从所有标注文件中提取类别
                if (_imageNames != null && _imageNames.Count > 0)
                {
                    foreach (var imageName in _imageNames)
                    {
                        try
                        {
                            var annotation = await _apiService.GetAnnotationAsync(imageName);
                            if (annotation?.Bboxes != null)
                            {
                                foreach (var bbox in annotation.Bboxes)
                                {
                                    if (!string.IsNullOrEmpty(bbox.ClassName))
                                    {
                                        allClassNames.Add(bbox.ClassName);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"读取标注文件 {imageName} 失败: {ex.Message}");
                        }
                    }
                }

                // 按字母顺序排序并更新_classes列表
                var sortedClasses = allClassNames.OrderBy(c => c).ToList();
                if (sortedClasses.Count > 0 && sortedClasses != _classes)
                {
                    _classes = sortedClasses;
                    SaveClasses();
                    Console.WriteLine($"[DEBUG] 类别列表已更新: {string.Join(", ", _classes)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新类别列表失败: {ex.Message}");
            }
        }

        private void BtnManageClasses_Click(object sender, RoutedEventArgs e)
        {
            var classManager = new ClassManagerDialog(_classes, (updatedClasses) =>
            {
                _classes.Clear();
                _classes.AddRange(updatedClasses);
                SaveClasses();
            }, (deletedClassName) =>
            {
                // 清理或更新所有使用该类别的标注框
                foreach (var bbox in _currentAnnotation.Bboxes)
                {
                    if (bbox.ClassName == deletedClassName)
                    {
                        bbox.ClassName = ""; // 清空类别名称
                        bbox.ClassId = -1; // 重置类别ID
                    }
                }

                // 重新绘制标注框
                DrawBoundingBoxes();

                // 更新标签列表
                UpdateLabelList();
            });
            classManager.ShowDialog();
        }

        private async Task LoadImagesAsync()
        {
            try
            {
                UpdateStatus("正在加载图片列表...");
                _imageNames = await _apiService.GetImagesAsync();

                // 创建 ImageFileInfo 对象列表，包含标注状态
                var fileInfoList = new List<ImageFileInfo>();
                var annotationsDir = System.IO.Path.Combine(_projectRootDir, "backend", "annotations");

                foreach (var imageName in _imageNames)
                {
                    var jsonPath = System.IO.Path.Combine(annotationsDir, System.IO.Path.GetFileNameWithoutExtension(imageName) + ".json");
                    var isAnnotated = File.Exists(jsonPath);
                    fileInfoList.Add(new ImageFileInfo { Name = imageName, IsAnnotated = isAnnotated });
                }

                // 更新界面
                LstImages.ItemsSource = fileInfoList;
                UpdateImageListStatus();
                UpdateStatus($"已加载 {_imageNames.Count} 张图片");

                // 从所有标注文件中更新类别列表
                await UpdateClassesFromAnnotations();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载图片列表失败: {ex.Message}");
                UpdateStatus("加载图片列表失败");
                MessageBox.Show(
                    $"加载图片列表失败: {ex.Message}\n\n请检查后端服务是否正常运行。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void LstImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstImages.SelectedItem == null) return;

            var selectedItem = LstImages.SelectedItem as ImageFileInfo;
            if (selectedItem == null) return;
            
            _currentImageName = selectedItem.Name;

            await LoadImageAsync(_currentImageName);
        }

        private async Task LoadImageAsync(string imageName)
        {
            UpdateStatus($"正在加载图片: {imageName}");

            try
            {
                var base64Image = await _apiService.GetImageBase64Async(imageName);
                if (string.IsNullOrEmpty(base64Image))
                {
                    UpdateStatus("加载图片失败");
                    return;
                }

                var imageBytes = Convert.FromBase64String(base64Image);
                
                // 使用 using 语句确保 MemoryStream 正确释放
                using (var stream = new MemoryStream(imageBytes))
                {
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = stream;
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad; // 立即加载到内存，避免延迟加载问题
                    bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // 冻结 BitmapImage，可以在跨线程使用

                    // 确保图片尺寸有效
                    if (bitmapImage.PixelWidth > 0 && bitmapImage.PixelHeight > 0)
                    {
                        CurrentImage.Source = bitmapImage;
                        CurrentImage.Width = bitmapImage.PixelWidth;
                        CurrentImage.Height = bitmapImage.PixelHeight;

                        AnnotationCanvas.Width = bitmapImage.PixelWidth;
                        AnnotationCanvas.Height = bitmapImage.PixelHeight;
                    }
                    else
                    {
                        UpdateStatus("图片尺寸无效");
                        return;
                    }

                    // 加载标注
                    _currentAnnotation = await _apiService.GetAnnotationAsync(imageName)
                        ?? new AnnotationData { ImageName = imageName, Bboxes = new List<BoundingBox>() };
                    _currentAnnotation.ImageName = imageName;
                    _currentAnnotation.Width = bitmapImage.PixelWidth;
                    _currentAnnotation.Height = bitmapImage.PixelHeight;

                    // 调试：打印加载到的标注框信息
                    Console.WriteLine($"[DEBUG] ========== 加载标注完成 ==========");
                    Console.WriteLine($"[DEBUG] 图片: {imageName}, 标注框数量: {_currentAnnotation.Bboxes.Count}");
                    for (int i = 0; i < _currentAnnotation.Bboxes.Count; i++)
                    {
                        var bbox = _currentAnnotation.Bboxes[i];
                        Console.WriteLine($"  [{i}] ClassName='{bbox.ClassName}', ClassId={bbox.ClassId}, X={bbox.X}, Y={bbox.Y}");
                    }
                    Console.WriteLine($"[DEBUG] ========== 加载标注结束 ==========");

                    DrawBoundingBoxes();
                    UpdateBboxList();
                    UpdateLabelList();
                    UpdateStatus($"已加载: {imageName}");

                    // 更新状态栏显示当前图片
                    var index = _imageNames.IndexOf(imageName);
                    StatusBarImage.Text = $"{imageName} ({index + 1}/{_imageNames.Count})";
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"加载图片失败: {ex.Message}");
                Console.WriteLine($"加载图片异常: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show(
                    $"加载图片失败: {ex.Message}\n\n请检查后端服务是否正常运行。",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void DrawBoundingBoxes()
        {
            BboxCanvas.Children.Clear();

            // 确保 CurrentImage 有有效的尺寸
            if (CurrentImage.Width <= 0 || CurrentImage.Height <= 0 || double.IsNaN(CurrentImage.Width) || double.IsNaN(CurrentImage.Height))
            {
                return;
            }

            foreach (var bbox in _currentAnnotation.Bboxes)
            {
                try
                {
                    // 根据标注类型绘制不同的形状
                    var annotationType = bbox.AnnotationType?.ToLower() ?? "bbox";
                    
                    switch (annotationType)
                    {
                        case "circle":
                            DrawCircleAnnotation(bbox);
                            break;
                        case "polygon":
                            DrawPolygonAnnotation(bbox);
                            break;
                        case "line":
                            DrawLineAnnotation(bbox);
                            break;
                        case "point":
                            DrawPointAnnotation(bbox);
                            break;
                        case "keypoints":
                        case "pose":
                            DrawKeypointsAnnotation(bbox);
                            break;
                        case "obb":
                        case "rotation":
                            DrawRotatedRectAnnotation(bbox);
                            break;
                        default:
                            DrawRectangleAnnotation(bbox);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // 捕获并忽略单个标注框的绘制错误
                    Console.WriteLine($"绘制标注框时出错: {ex.Message}");
                }
            }
        }

        private void DrawRectangleAnnotation(BoundingBox bbox)
        {
            var x = (bbox.X - bbox.Width / 2) * CurrentImage.Width;
            var y = (bbox.Y - bbox.Height / 2) * CurrentImage.Height;
            var width = bbox.Width * CurrentImage.Width;
            var height = bbox.Height * CurrentImage.Height;

            // 检查计算结果是否有效
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(width) || double.IsNaN(height) ||
                double.IsInfinity(x) || double.IsInfinity(y) || double.IsInfinity(width) || double.IsInfinity(height) ||
                width <= 0 || height <= 0)
            {
                return; // 跳过无效的标注框
            }

            var isSelected = _selectedBboxes.Contains(bbox);
            var colorHex = !string.IsNullOrEmpty(bbox.Color) ? bbox.Color : GetClassColor(bbox.ClassName);

            // 解析颜色
            var borderColor = (Color)ColorConverter.ConvertFromString(colorHex);
            var fillColor = Color.FromArgb(80, borderColor.R, borderColor.G, borderColor.B);

            var rect = new Rectangle
            {
                Stroke = isSelected ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : new SolidColorBrush(borderColor),
                StrokeThickness = isSelected ? 3 : 2,
                Fill = new SolidColorBrush(isSelected ? Color.FromArgb(100, 255, 200, 0) : fillColor)
            };

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = width;
            rect.Height = height;

            // 存储标注框数据到 Tag，用于点击选中
            rect.Tag = bbox;
            rect.MouseLeftButtonDown += Bbox_MouseLeftButtonDown;

            BboxCanvas.Children.Add(rect);

            // 添加类别标签
            AddAnnotationLabel(bbox, x, y, height, borderColor);

            // 如果是选中状态，添加调整手柄
            if (isSelected)
            {
                DrawResizeHandles(x, y, width, height);
            }
        }

        private void DrawCircleAnnotation(BoundingBox bbox)
        {
            if (bbox.Points == null || bbox.Points.Count < 3)
            {
                // 如果没有点数据，尝试使用 Width/Height 绘制
                DrawRectangleAnnotation(bbox);
                return;
            }

            var centerX = bbox.X * CurrentImage.Width;
            var centerY = bbox.Y * CurrentImage.Height;
            var radius = bbox.Width * CurrentImage.Width / 2;

            if (radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius))
            {
                return;
            }

            var isSelected = _selectedBboxes.Contains(bbox);
            var colorHex = !string.IsNullOrEmpty(bbox.Color) ? bbox.Color : GetClassColor(bbox.ClassName);
            var borderColor = (Color)ColorConverter.ConvertFromString(colorHex);
            var fillColor = Color.FromArgb(80, borderColor.R, borderColor.G, borderColor.B);

            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = isSelected ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : new SolidColorBrush(borderColor),
                StrokeThickness = isSelected ? 3 : 2,
                Fill = new SolidColorBrush(isSelected ? Color.FromArgb(100, 255, 200, 0) : fillColor),
                Stretch = Stretch.Uniform
            };

            Canvas.SetLeft(ellipse, centerX - radius);
            Canvas.SetTop(ellipse, centerY - radius);

            ellipse.Tag = bbox;
            ellipse.MouseLeftButtonDown += Bbox_MouseLeftButtonDown;

            BboxCanvas.Children.Add(ellipse);

            // 添加类别标签
            AddAnnotationLabel(bbox, centerX - radius, centerY - radius, radius * 2, borderColor);

            // 如果是选中状态，添加调整手柄
            if (isSelected)
            {
                DrawResizeHandles(centerX - radius, centerY - radius, radius * 2, radius * 2);
            }
        }

        private void DrawPolygonAnnotation(BoundingBox bbox)
        {
            if (bbox.Points == null || bbox.Points.Count < 3)
            {
                return;
            }

            var isSelected = _selectedBboxes.Contains(bbox);
            var colorHex = !string.IsNullOrEmpty(bbox.Color) ? bbox.Color : GetClassColor(bbox.ClassName);
            var borderColor = (Color)ColorConverter.ConvertFromString(colorHex);
            var fillColor = Color.FromArgb(80, borderColor.R, borderColor.G, borderColor.B);

            var polygon = new Polygon
            {
                Stroke = isSelected ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : new SolidColorBrush(borderColor),
                StrokeThickness = isSelected ? 3 : 2,
                Fill = new SolidColorBrush(isSelected ? Color.FromArgb(100, 255, 200, 0) : fillColor)
            };

            foreach (var point in bbox.Points)
            {
                if (point.Count >= 2)
                {
                    polygon.Points.Add(new Point(point[0] * CurrentImage.Width, point[1] * CurrentImage.Height));
                }
            }

            polygon.Tag = bbox;
            polygon.MouseLeftButtonDown += Bbox_MouseLeftButtonDown;

            BboxCanvas.Children.Add(polygon);

            // 添加类别标签（使用第一个点的位置）
            if (bbox.Points.Count > 0 && bbox.Points[0].Count >= 2)
            {
                var labelX = bbox.Points[0][0] * CurrentImage.Width;
                var labelY = bbox.Points[0][1] * CurrentImage.Height;
                AddAnnotationLabel(bbox, labelX, labelY, 0, borderColor);
            }
        }

        private void DrawLineAnnotation(BoundingBox bbox)
        {
            if (bbox.Points == null || bbox.Points.Count < 2)
            {
                return;
            }

            var isSelected = _selectedBboxes.Contains(bbox);
            var colorHex = !string.IsNullOrEmpty(bbox.Color) ? bbox.Color : GetClassColor(bbox.ClassName);
            var borderColor = (Color)ColorConverter.ConvertFromString(colorHex);

            var line = new Line
            {
                X1 = bbox.Points[0][0] * CurrentImage.Width,
                Y1 = bbox.Points[0][1] * CurrentImage.Height,
                X2 = bbox.Points[1][0] * CurrentImage.Width,
                Y2 = bbox.Points[1][1] * CurrentImage.Height,
                Stroke = isSelected ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : new SolidColorBrush(borderColor),
                StrokeThickness = isSelected ? 4 : 3
            };

            line.Tag = bbox;
            line.MouseLeftButtonDown += Bbox_MouseLeftButtonDown;

            BboxCanvas.Children.Add(line);

            // 添加类别标签
            var labelX = bbox.Points[0][0] * CurrentImage.Width;
            var labelY = bbox.Points[0][1] * CurrentImage.Height;
            AddAnnotationLabel(bbox, labelX, labelY, 0, borderColor);
        }

        private void DrawPointAnnotation(BoundingBox bbox)
        {
            if (bbox.Points == null || bbox.Points.Count < 1 || bbox.Points[0].Count < 2)
            {
                return;
            }

            var isSelected = _selectedBboxes.Contains(bbox);
            var colorHex = !string.IsNullOrEmpty(bbox.Color) ? bbox.Color : GetClassColor(bbox.ClassName);
            var borderColor = (Color)ColorConverter.ConvertFromString(colorHex);

            var ellipse = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = isSelected ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : new SolidColorBrush(borderColor),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2,
                Stretch = Stretch.Uniform
            };

            var x = bbox.Points[0][0] * CurrentImage.Width;
            var y = bbox.Points[0][1] * CurrentImage.Height;

            Canvas.SetLeft(ellipse, x - 5);
            Canvas.SetTop(ellipse, y - 5);

            ellipse.Tag = bbox;
            ellipse.MouseLeftButtonDown += Bbox_MouseLeftButtonDown;

            BboxCanvas.Children.Add(ellipse);

            // 添加类别标签
            AddAnnotationLabel(bbox, x, y, 0, borderColor);
        }

        private void DrawRotatedRectAnnotation(BoundingBox bbox)
        {
            // 旋转框暂时使用矩形绘制
            // TODO: 实现真正的旋转矩形绘制
            DrawRectangleAnnotation(bbox);
        }

        private void DrawKeypointsAnnotation(BoundingBox bbox)
        {
            if (bbox.Keypoints == null || bbox.Keypoints.Count == 0)
            {
                return;
            }

            var isSelected = _selectedBboxes.Contains(bbox);
            var colorHex = !string.IsNullOrEmpty(bbox.Color) ? bbox.Color : GetClassColor(bbox.ClassName);
            var borderColor = (Color)ColorConverter.ConvertFromString(colorHex);

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
                            X1 = startPoint[0] * CurrentImage.Width,
                            Y1 = startPoint[1] * CurrentImage.Height,
                            X2 = endPoint[0] * CurrentImage.Width,
                            Y2 = endPoint[1] * CurrentImage.Height,
                            Stroke = isSelected ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : new SolidColorBrush(borderColor),
                            StrokeThickness = isSelected ? 3 : 2
                        };
                        
                        line.Tag = bbox;
                        line.MouseLeftButtonDown += Bbox_MouseLeftButtonDown;
                        BboxCanvas.Children.Add(line);
                    }
                }
            }

            // 绘制关键点
            for (int i = 0; i < bbox.Keypoints.Count; i++)
            {
                var kpt = bbox.Keypoints[i];
                if (kpt.Count >= 2 && (kpt[0] > 0 || kpt[1] > 0))
                {
                    var ellipse = new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = isSelected ? new SolidColorBrush(Color.FromRgb(255, 200, 0)) : new SolidColorBrush(borderColor),
                        Stroke = Brushes.White,
                        StrokeThickness = 1
                    };
                    
                    var px = kpt[0] * CurrentImage.Width;
                    var py = kpt[1] * CurrentImage.Height;
                    
                    Canvas.SetLeft(ellipse, px - 4);
                    Canvas.SetTop(ellipse, py - 4);
                    
                    ellipse.Tag = bbox;
                    ellipse.MouseLeftButtonDown += Bbox_MouseLeftButtonDown;
                    BboxCanvas.Children.Add(ellipse);
                }
            }

            // 添加类别标签
            if (bbox.Keypoints.Count > 0 && bbox.Keypoints[0].Count >= 2)
            {
                var labelX = bbox.Keypoints[0][0] * CurrentImage.Width;
                var labelY = bbox.Keypoints[0][1] * CurrentImage.Height;
                AddAnnotationLabel(bbox, labelX, labelY, 0, borderColor);
            }
        }

        private void AddAnnotationLabel(BoundingBox bbox, double x, double y, double height, Color borderColor)
        {
            var textBlock = new TextBlock
            {
                Text = $"{bbox.ClassName} ({bbox.Confidence:F2})",
                Foreground = new SolidColorBrush(borderColor),
                Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
                Padding = new Thickness(4, 2, 4, 2),
                FontWeight = FontWeights.Normal,
                TextWrapping = TextWrapping.NoWrap
            };

            // 确保标签在画布内显示
            var textY = Math.Max(0, y - 22);
            if (textY + 20 > CurrentImage.Height)
            {
                // 如果上方空间不足，放在标注框内部底部
                textY = Math.Max(0, y + height - 20);
            }

            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, textY);

            BboxCanvas.Children.Add(textBlock);
        }

        private void DrawResizeHandles(double x, double y, double width, double height)
        {
            var handleSize = 8;
            var halfHandle = handleSize / 2;
            var handleColor = new SolidColorBrush(Color.FromRgb(255, 200, 0));
            var handleBorder = new SolidColorBrush(Color.FromRgb(0, 0, 0));

            // 8个调整手柄位置：四角 + 四边中点
            var handlePositions = new[]
            {
                new { Index = 0, X = x - halfHandle, Y = y - halfHandle, Cursor = Cursors.SizeNWSE }, // 左上
                new { Index = 1, X = x + width / 2 - halfHandle, Y = y - halfHandle, Cursor = Cursors.SizeNS }, // 上中
                new { Index = 2, X = x + width - halfHandle, Y = y - halfHandle, Cursor = Cursors.SizeNESW }, // 右上
                new { Index = 3, X = x + width - halfHandle, Y = y + height / 2 - halfHandle, Cursor = Cursors.SizeWE }, // 右中
                new { Index = 4, X = x + width - halfHandle, Y = y + height - halfHandle, Cursor = Cursors.SizeNWSE }, // 右下
                new { Index = 5, X = x + width / 2 - halfHandle, Y = y + height - halfHandle, Cursor = Cursors.SizeNS }, // 下中
                new { Index = 6, X = x - halfHandle, Y = y + height - halfHandle, Cursor = Cursors.SizeNESW }, // 左下
                new { Index = 7, X = x - halfHandle, Y = y + height / 2 - halfHandle, Cursor = Cursors.SizeWE }  // 左中
            };

            foreach (var pos in handlePositions)
            {
                var handle = new Ellipse
                {
                    Width = handleSize,
                    Height = handleSize,
                    Fill = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                    Stroke = handleColor,
                    StrokeThickness = 2,
                    Cursor = pos.Cursor,
                    Tag = pos.Index,
                    Stretch = Stretch.Uniform
                };

                Canvas.SetLeft(handle, pos.X);
                Canvas.SetTop(handle, pos.Y);

                handle.MouseLeftButtonDown += ResizeHandle_MouseLeftButtonDown;
                handle.MouseMove += ResizeHandle_MouseMove;
                handle.MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;

                BboxCanvas.Children.Add(handle);
            }
        }

        private void Bbox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 支持多种形状类型
            BoundingBox? bbox = null;
            
            if (sender is Rectangle rect && rect.Tag is BoundingBox)
            {
                bbox = rect.Tag as BoundingBox;
            }
            else if (sender is Ellipse ellipse && ellipse.Tag is BoundingBox)
            {
                bbox = ellipse.Tag as BoundingBox;
            }
            else if (sender is Polygon polygon && polygon.Tag is BoundingBox)
            {
                bbox = polygon.Tag as BoundingBox;
            }
            else if (sender is Line line && line.Tag is BoundingBox)
            {
                bbox = line.Tag as BoundingBox;
            }

            if (bbox != null)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    // Ctrl+点击：切换选中状态
                    if (_selectedBboxes.Contains(bbox))
                    {
                        _selectedBboxes.Remove(bbox);
                    }
                    else
                    {
                        _selectedBboxes.Add(bbox);
                    }
                    _selectedBbox = _selectedBboxes.Count > 0 ? _selectedBboxes[0] : null;
                }
                else
                {
                    // 普通点击：选中当前，清除其他
                    _selectedBboxes.Clear();
                    _selectedBboxes.Add(bbox);
                    _selectedBbox = bbox;
                }
                
                DrawBoundingBoxes();
                UpdateBboxList();

                // 更新选中状态
                var displayItem = LstBboxes.Items.Cast<BoundingBoxDisplayItem>().FirstOrDefault(item => item.Bbox == bbox);
                if (displayItem != null)
                {
                    LstBboxes.SelectedItem = displayItem;
                }
                e.Handled = true;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(AnnotationCanvas);

            // 处理调整标注框大小
            if (_isResizing && _selectedBbox != null)
            {
                var currentPos = e.GetPosition(AnnotationCanvas);
                var deltaX = currentPos.X - _resizeStartPos.X;
                var deltaY = currentPos.Y - _resizeStartPos.Y;

                // 根据手柄索引调整大小
                switch (_resizeHandleIndex)
                {
                    case 0: // 左上
                        _selectedBbox.X = (float)((_originalCenter.X + deltaX + _originalWidth / 2) / CurrentImage.Width);
                        _selectedBbox.Y = (float)((_originalCenter.Y + deltaY + _originalHeight / 2) / CurrentImage.Height);
                        _selectedBbox.Width = (float)(_originalWidth - deltaX / CurrentImage.Width);
                        _selectedBbox.Height = (float)(_originalHeight - deltaY / CurrentImage.Height);
                        break;
                    case 1: // 上中
                        _selectedBbox.Y = (float)((_originalCenter.Y + deltaY + _originalHeight / 2) / CurrentImage.Height);
                        _selectedBbox.Height = (float)(_originalHeight - deltaY / CurrentImage.Height);
                        break;
                    case 2: // 右上
                        _selectedBbox.Y = (float)((_originalCenter.Y + deltaY + _originalHeight / 2) / CurrentImage.Height);
                        _selectedBbox.Width = (float)(_originalWidth + deltaX / CurrentImage.Width);
                        _selectedBbox.Height = (float)(_originalHeight - deltaY / CurrentImage.Height);
                        break;
                    case 3: // 右中
                        _selectedBbox.Width = (float)(_originalWidth + deltaX / CurrentImage.Width);
                        break;
                    case 4: // 右下
                        _selectedBbox.Width = (float)(_originalWidth + deltaX / CurrentImage.Width);
                        _selectedBbox.Height = (float)(_originalHeight + deltaY / CurrentImage.Height);
                        break;
                    case 5: // 下中
                        _selectedBbox.Height = (float)(_originalHeight + deltaY / CurrentImage.Height);
                        break;
                    case 6: // 左下
                        _selectedBbox.X = (float)((_originalCenter.X + deltaX + _originalWidth / 2) / CurrentImage.Width);
                        _selectedBbox.Width = (float)(_originalWidth - deltaX / CurrentImage.Width);
                        _selectedBbox.Height = (float)(_originalHeight + deltaY / CurrentImage.Height);
                        break;
                    case 7: // 左中
                        _selectedBbox.X = (float)((_originalCenter.X + deltaX + _originalWidth / 2) / CurrentImage.Width);
                        _selectedBbox.Width = (float)(_originalWidth - deltaX / CurrentImage.Width);
                        break;
                }

                // 确保最小尺寸
                _selectedBbox.Width = Math.Max(0.01f, _selectedBbox.Width);
                _selectedBbox.Height = Math.Max(0.01f, _selectedBbox.Height);

                DrawBoundingBoxes();
                return;
            }

            // 处理拖动
            if (_isPanning && _currentTool == AnnotationTool.Pan)
            {
                var currentPos = e.GetPosition(CanvasContainer);
                var deltaX = currentPos.X - _panStartPos.X;
                var deltaY = currentPos.Y - _panStartPos.Y;

                // 更新偏移量
                _panOffset.X += deltaX;
                _panOffset.Y += deltaY;

                // 应用变换
                var transform = new TranslateTransform(_panOffset.X, _panOffset.Y);
                AnnotationCanvas.RenderTransform = transform;

                // 更新起始点
                _panStartPos = currentPos;
                return;
            }

            // 更新十字光标
            UpdateCrosshair(pos);

            // 更新坐标显示
            if (CurrentImage.Width > 0 && CurrentImage.Height > 0)
            {
                var relativeX = (pos.X / CurrentImage.Width).ToString("F2");
                var relativeY = (pos.Y / CurrentImage.Height).ToString("F2");
                TxtMousePosition.Text = $"X: {relativeX} Y: {relativeY}";
            }

            // 处理绘制逻辑
            if (!_isDrawing) return;

            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    UpdateRectangleDrawing(pos);
                    break;
                case AnnotationTool.Polygon:
                    UpdatePolygonDrawing(pos);
                    break;
                case AnnotationTool.RotateRect:
                    UpdateRotateRectDrawing(pos);
                    break;
                case AnnotationTool.Circle:
                    UpdateCircleDrawing(pos);
                    break;
                case AnnotationTool.Line:
                    UpdateLineDrawing(pos);
                    break;
            }
        }

        private void UpdateCrosshair(Point pos)
        {
            var imageWidth = CurrentImage.Width > 0 ? CurrentImage.Width : 1;
            var imageHeight = CurrentImage.Height > 0 ? CurrentImage.Height : 1;

            // 水平线
            CrossHairH.X1 = 0;
            CrossHairH.Y1 = pos.Y;
            CrossHairH.X2 = imageWidth;
            CrossHairH.Y2 = pos.Y;
            CrossHairH.Visibility = Visibility.Visible;

            // 垂直线
            CrossHairV.X1 = pos.X;
            CrossHairV.Y1 = 0;
            CrossHairV.X2 = pos.X;
            CrossHairV.Y2 = imageHeight;
            CrossHairV.Visibility = Visibility.Visible;
        }

        private void UpdateRectangleDrawing(Point pos)
        {
            if (_currentRect == null) return;

            var x = Math.Min(pos.X, _startPoint.X);
            var y = Math.Min(pos.Y, _startPoint.Y);
            var width = Math.Abs(pos.X - _startPoint.X);
            var height = Math.Abs(pos.Y - _startPoint.Y);

            Canvas.SetLeft(_currentRect, x);
            Canvas.SetTop(_currentRect, y);
            _currentRect.Width = width;
            _currentRect.Height = height;
        }

        private void UpdatePolygonDrawing(Point pos)
        {
            if (_polygonPoints.Count > 0)
            {
                // 更新最后一条线
                if (_polygonLines.Count > 0)
                {
                    var lastLine = _polygonLines.Last();
                    var lastPoint = _polygonPoints.Last();
                    lastLine.X2 = pos.X;
                    lastLine.Y2 = pos.Y;
                }
            }
        }

        private void UpdateRotateRectDrawing(Point pos)
        {
            if (_rotateRectPreview == null) return;

            // 计算宽度和高度
            var dx = pos.X - _rotateRectCenter.X;
            var dy = pos.Y - _rotateRectCenter.Y;
            _rotateRectAngle = Math.Atan2(dy, dx);
            _rotateRectWidth = 2 * Math.Sqrt(dx * dx + dy * dy);
            _rotateRectHeight = _rotateRectWidth * 0.6; // 默认高度比例

            // 更新预览矩形
            UpdateRotateRectPreview();
        }

        private void UpdateRotateRectPreview()
        {
            if (_rotateRectPreview == null) return;

            // 清除旧的预览
            BboxCanvas.Children.Remove(_rotateRectPreview);
            BboxCanvas.Children.Remove(_rotateRectDirection);

            // 创建旋转的矩形
            var rect = new RotateTransform(_rotateRectAngle * 180 / Math.PI, _rotateRectCenter.X, _rotateRectCenter.Y);

            _rotateRectPreview = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)),
                Width = _rotateRectWidth,
                Height = _rotateRectHeight,
                RenderTransform = rect
            };

            Canvas.SetLeft(_rotateRectPreview, _rotateRectCenter.X - _rotateRectWidth / 2);
            Canvas.SetTop(_rotateRectPreview, _rotateRectCenter.Y - _rotateRectHeight / 2);

            // 添加方向指示线
            _rotateRectDirection = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(255, 200, 0)),
                StrokeThickness = 2,
                X1 = _rotateRectCenter.X,
                Y1 = _rotateRectCenter.Y,
                X2 = _rotateRectCenter.X + Math.Cos(_rotateRectAngle) * _rotateRectWidth / 2,
                Y2 = _rotateRectCenter.Y + Math.Sin(_rotateRectAngle) * _rotateRectWidth / 2
            };

            BboxCanvas.Children.Add(_rotateRectPreview);
            BboxCanvas.Children.Add(_rotateRectDirection);
        }

        private void UpdateCircleDrawing(Point pos)
        {
            if (_currentCircle == null) return;

            // 计算半径
            var dx = pos.X - _startPoint.X;
            var dy = pos.Y - _startPoint.Y;
            var radius = Math.Sqrt(dx * dx + dy * dy);

            // 更新圆形 - 强制保持正圆
            _currentCircle.Width = radius * 2;
            _currentCircle.Height = radius * 2;
            Canvas.SetLeft(_currentCircle, _startPoint.X - radius);
            Canvas.SetTop(_currentCircle, _startPoint.Y - radius);
        }

        private void UpdateLineDrawing(Point pos)
        {
            if (_currentLine == null) return;

            // 更新线段终点
            _currentLine.X2 = pos.X;
            _currentLine.Y2 = pos.Y;
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e)
        {
            // 隐藏十字光标
            CrossHairH.Visibility = Visibility.Collapsed;
            CrossHairV.Visibility = Visibility.Collapsed;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 如果正在调整标注框大小，调用手柄释放事件
            if (_isResizing)
            {
                _isResizing = false;
                _resizeHandleIndex = -1;
                UpdateBboxList();
            }

            // 结束拖动
            if (_isPanning && _currentTool == AnnotationTool.Pan)
            {
                _isPanning = false;
                AnnotationCanvas.Cursor = Cursors.Arrow;
                return;
            }

            if (!_isDrawing) return;

            var pos = e.GetPosition(AnnotationCanvas);

            switch (_currentTool)
            {
                case AnnotationTool.Rectangle:
                    HandleRectangleRelease(pos);
                    break;
                case AnnotationTool.Polygon:
                    HandlePolygonClick(pos);
                    return; // 多边形是连续点击，不结束绘制
                case AnnotationTool.Keypoints:
                    HandleKeypointClick(pos);
                    return; // 关键点是连续点击，不结束绘制
                case AnnotationTool.RotateRect:
                    HandleRotateRectRelease(pos);
                    break;
                case AnnotationTool.Circle:
                    HandleCircleRelease(pos);
                    break;
                case AnnotationTool.Line:
                    HandleLineRelease(pos);
                    break;
                case AnnotationTool.Eraser:
                    HandleEraserClick(pos);
                    break;
            }

            _isDrawing = false;
            _currentRect = null;
            _currentCircle = null;
            _currentLine = null;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(AnnotationCanvas);
            _startPoint = pos;
            _isDrawing = true;

            switch (_currentTool)
            {
                case AnnotationTool.Pan:
                    // 开始拖动
                    _isPanning = true;
                    _panStartPos = e.GetPosition(CanvasContainer);
                    AnnotationCanvas.Cursor = Cursors.Hand;
                    return; // 不进行绘制

                case AnnotationTool.Rectangle:
                    // 开始绘制矩形
                    _currentRect = new Rectangle
                    {
                        Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(50, 0, 120, 215))
                    };
                    Canvas.SetLeft(_currentRect, pos.X);
                    Canvas.SetTop(_currentRect, pos.Y);
                    BboxCanvas.Children.Add(_currentRect);
                    break;

                case AnnotationTool.Polygon:
                    HandlePolygonClick(pos);
                    return; // 多边形不结束绘制状态

                case AnnotationTool.Keypoints:
                    HandleKeypointClick(pos);
                    return; // 关键点不结束绘制状态

                case AnnotationTool.RotateRect:
                    // 开始绘制旋转框 - 第一点击确定中心
                    _rotateRectCenter = pos;
                    _rotateRectStart = pos;
                    _rotateRectPreview = new Rectangle
                    {
                        Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)),
                        Width = 0,
                        Height = 0
                    };
                    Canvas.SetLeft(_rotateRectPreview, pos.X);
                    Canvas.SetTop(_rotateRectPreview, pos.Y);
                    BboxCanvas.Children.Add(_rotateRectPreview);
                    break;

                case AnnotationTool.Circle:
                    // 开始绘制圆形
                    _currentCircle = new Ellipse
                    {
                        Stroke = new SolidColorBrush(Color.FromRgb(6, 182, 212)),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(50, 6, 182, 212)),
                        Stretch = Stretch.Uniform
                    };
                    Canvas.SetLeft(_currentCircle, pos.X);
                    Canvas.SetTop(_currentCircle, pos.Y);
                    BboxCanvas.Children.Add(_currentCircle);
                    break;

                case AnnotationTool.Line:
                    // 开始绘制线段
                    _currentLine = new Line
                    {
                        X1 = pos.X,
                        Y1 = pos.Y,
                        X2 = pos.X,
                        Y2 = pos.Y,
                        Stroke = new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                        StrokeThickness = 2
                    };
                    BboxCanvas.Children.Add(_currentLine);
                    break;

                case AnnotationTool.Point:
                    // 直接创建点标注
                    HandlePointClick(pos);
                    _isDrawing = false;
                    return;

                case AnnotationTool.Eraser:
                    _currentRect = new Rectangle
                    {
                        Stroke = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(50, 239, 68, 68))
                    };
                    Canvas.SetLeft(_currentRect, pos.X - 10);
                    Canvas.SetTop(_currentRect, pos.Y - 10);
                    _currentRect.Width = 20;
                    _currentRect.Height = 20;
                    BboxCanvas.Children.Add(_currentRect);
                    break;
            }
        }

        private void HandleRectangleRelease(Point pos)
        {
            if (_currentRect == null) return;

            var x = Math.Min(pos.X, _startPoint.X);
            var y = Math.Min(pos.Y, _startPoint.Y);
            var width = Math.Abs(pos.X - _startPoint.X);
            var height = Math.Abs(pos.Y - _startPoint.Y);

            // 如果框太小，忽略
            if (width < 5 || height < 5)
            {
                BboxCanvas.Children.Remove(_currentRect);
                _currentRect = null;
                return;
            }

            BboxCanvas.Children.Remove(_currentRect);
            _currentRect = null;

            // 自动吸附到边缘（吸附距离为10像素）
            const int snapDistance = 10;
            var imageWidth = CurrentImage.Width;
            var imageHeight = CurrentImage.Height;

            // 吸附到左边和右边
            if (x <= snapDistance)
            {
                x = 0;
                if (x + width >= imageWidth - snapDistance)
                {
                    width = imageWidth;
                }
            }
            else if (x + width >= imageWidth - snapDistance)
            {
                width = imageWidth - x;
            }

            // 吸附到上边和下边
            if (y <= snapDistance)
            {
                y = 0;
                if (y + height >= imageHeight - snapDistance)
                {
                    height = imageHeight;
                }
            }
            else if (y + height >= imageHeight - snapDistance)
            {
                height = imageHeight - y;
            }

            // 弹出类别输入对话框
            var classDialog = new ClassSelectDialog(_classes);
            if (classDialog.ShowDialog() == true)
            {
                var className = classDialog.SelectedClass;
                
                // 验证类别名称不为空
                if (string.IsNullOrWhiteSpace(className))
                {
                    MessageBox.Show("选择的类别为空，请重新选择类别", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Console.WriteLine($"[DEBUG] ClassSelectDialog 返回的类别: '{className}'");

                // 如果类别不存在，自动添加
                if (!_classes.Contains(className))
                {
                    _classes.Add(className);
                    SaveClasses();
                    UpdateStatus($"已添加新类别: {className}");
                }

                var bbox = new BoundingBox
                {
                    X = (float)((x + width / 2) / CurrentImage.Width),
                    Y = (float)((y + height / 2) / CurrentImage.Height),
                    Width = (float)(width / CurrentImage.Width),
                    Height = (float)(height / CurrentImage.Height),
                    ClassId = _classes.IndexOf(className),
                    ClassName = className,
                    Confidence = 1.0f
                };

                Console.WriteLine($"[DEBUG] 创建新标注框: ClassName='{bbox.ClassName}', ClassId={bbox.ClassId}, 总类别数={_classes.Count}");
                Console.WriteLine($"[DEBUG] 当前类别列表: {string.Join(", ", _classes)}");

                _currentAnnotation.Bboxes.Add(bbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateLabelList();
            }
            else
            {
                Console.WriteLine("[DEBUG] 用户取消了类别选择对话框");
            }
        }

        private void HandlePolygonClick(Point pos)
        {
            // 添加点到多边形
            _polygonPoints.Add(pos);

            // 添加顶点标记
            var vertex = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(255, 255, 0)),
                Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                StrokeThickness = 2,
                Stretch = Stretch.Uniform
            };
            Canvas.SetLeft(vertex, pos.X - 4);
            Canvas.SetTop(vertex, pos.Y - 4);
            BboxCanvas.Children.Add(vertex);
            _polygonVertices.Add(vertex);

            // 添加连接线
            if (_polygonPoints.Count > 1)
            {
                var line = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                    StrokeThickness = 2
                };
                var prevPoint = _polygonPoints[_polygonPoints.Count - 2];
                line.X1 = prevPoint.X;
                line.Y1 = prevPoint.Y;
                line.X2 = pos.X;
                line.Y2 = pos.Y;
                BboxCanvas.Children.Add(line);
                _polygonLines.Add(line);
            }

            UpdateStatus($"多边形: {_polygonPoints.Count} 个点 (右键完成)");
        }

        private void HandleKeypointClick(Point pos)
        {
            // 添加关键点
            _keypoints.Add(pos);

            // 添加标记
            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
                Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                StrokeThickness = 2,
                Stretch = Stretch.Uniform
            };
            Canvas.SetLeft(marker, pos.X - 4);
            Canvas.SetTop(marker, pos.Y - 4);
            BboxCanvas.Children.Add(marker);
            _keypointMarkers.Add(marker);

            UpdateStatus($"关键点: {_keypoints.Count} 个点 (右键完成)");
        }

        private void HandlePointClick(Point pos)
        {
            // 弹出类别输入对话框
            var classDialog = new ClassSelectDialog(_classes);
            if (classDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(classDialog.SelectedClass))
            {
                var className = classDialog.SelectedClass;

                if (!_classes.Contains(className))
                {
                    _classes.Add(className);
                }

                // 创建点标注
                var bbox = new BoundingBox
                {
                    X = (float)(pos.X / CurrentImage.Width),
                    Y = (float)(pos.Y / CurrentImage.Height),
                    Width = 0.01f,  // 点的宽度很小
                    Height = 0.01f,
                    ClassId = _classes.IndexOf(className),
                    ClassName = className,
                    AnnotationType = "point",
                    Points = new List<List<float>> { new List<float> { (float)(pos.X / CurrentImage.Width), (float)(pos.Y / CurrentImage.Height) } }
                };

                _currentAnnotation.Bboxes.Add(bbox);
                DrawBoundingBoxes();
                UpdateStatus($"已添加点标注: {className}");
            }
        }

        private void HandleCircleRelease(Point pos)
        {
            if (_currentCircle == null) return;

            // 计算半径
            var dx = pos.X - _startPoint.X;
            var dy = pos.Y - _startPoint.Y;
            var radius = Math.Sqrt(dx * dx + dy * dy);

            // 如果圆太小，忽略
            if (radius < 5)
            {
                BboxCanvas.Children.Remove(_currentCircle);
                _currentCircle = null;
                return;
            }

            BboxCanvas.Children.Remove(_currentCircle);
            _currentCircle = null;

            // 弹出类别输入对话框
            var classDialog = new ClassSelectDialog(_classes);
            if (classDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(classDialog.SelectedClass))
            {
                var className = classDialog.SelectedClass;

                if (!_classes.Contains(className))
                {
                    _classes.Add(className);
                    SaveClasses();
                }

                // 生成圆形的多边形近似（32个点）
                var points = new List<List<float>>();
                int segments = 32;
                for (int i = 0; i < segments; i++)
                {
                    var angle = 2 * Math.PI * i / segments;
                    var x = _startPoint.X + radius * Math.Cos(angle);
                    var y = _startPoint.Y + radius * Math.Sin(angle);
                    points.Add(new List<float> { (float)(x / CurrentImage.Width), (float)(y / CurrentImage.Height) });
                }

                var bbox = new BoundingBox
                {
                    X = (float)(_startPoint.X / CurrentImage.Width),
                    Y = (float)(_startPoint.Y / CurrentImage.Height),
                    Width = (float)(radius * 2 / CurrentImage.Width),
                    Height = (float)(radius * 2 / CurrentImage.Height),
                    ClassId = _classes.IndexOf(className),
                    ClassName = className,
                    AnnotationType = "circle",
                    Points = points
                };

                _currentAnnotation.Bboxes.Add(bbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateLabelList();
                UpdateStatus($"已添加圆形标注: {className}");
            }
        }

        private void HandleLineRelease(Point pos)
        {
            if (_currentLine == null) return;

            // 计算线段长度
            var dx = pos.X - _startPoint.X;
            var dy = pos.Y - _startPoint.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);

            // 如果线段太短，忽略
            if (length < 5)
            {
                BboxCanvas.Children.Remove(_currentLine);
                _currentLine = null;
                return;
            }

            BboxCanvas.Children.Remove(_currentLine);
            _currentLine = null;

            // 弹出类别输入对话框
            var classDialog = new ClassSelectDialog(_classes);
            if (classDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(classDialog.SelectedClass))
            {
                var className = classDialog.SelectedClass;

                if (!_classes.Contains(className))
                {
                    _classes.Add(className);
                    SaveClasses();
                }

                var bbox = new BoundingBox
                {
                    X = (float)((_startPoint.X + pos.X) / 2 / CurrentImage.Width),
                    Y = (float)((_startPoint.Y + pos.Y) / 2 / CurrentImage.Height),
                    Width = (float)(Math.Abs(dx) / CurrentImage.Width),
                    Height = (float)(Math.Abs(dy) / CurrentImage.Height),
                    ClassId = _classes.IndexOf(className),
                    ClassName = className,
                    AnnotationType = "line",
                    Points = new List<List<float>>
                    {
                        new List<float> { (float)(_startPoint.X / CurrentImage.Width), (float)(_startPoint.Y / CurrentImage.Height) },
                        new List<float> { (float)(pos.X / CurrentImage.Width), (float)(pos.Y / CurrentImage.Height) }
                    }
                };

                _currentAnnotation.Bboxes.Add(bbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateLabelList();
                UpdateStatus($"已添加线段标注: {className}");
            }
        }

        private void HandleRotateRectRelease(Point pos)
        {
            if (_rotateRectPreview == null) return;

            // 清除预览
            BboxCanvas.Children.Remove(_rotateRectPreview);
            BboxCanvas.Children.Remove(_rotateRectDirection);

            // 检查大小
            if (_rotateRectWidth < 10 || _rotateRectHeight < 10)
            {
                _rotateRectPreview = null;
                _rotateRectDirection = null;
                return;
            }

            // 弹出类别输入对话框
            var classDialog = new ClassSelectDialog(_classes);
            if (classDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(classDialog.SelectedClass))
            {
                var className = classDialog.SelectedClass;

                if (!_classes.Contains(className))
                {
                    _classes.Add(className);
                    SaveClasses();
                    UpdateStatus($"已添加新类别: {className}");
                }

                // 保存完整的 OBB 数据（包括旋转角度）
                var bbox = new BoundingBox
                {
                    X = (float)(_rotateRectCenter.X / CurrentImage.Width),
                    Y = (float)(_rotateRectCenter.Y / CurrentImage.Height),
                    Width = (float)(_rotateRectWidth / CurrentImage.Width),
                    Height = (float)(_rotateRectHeight / CurrentImage.Height),
                    ClassId = _classes.IndexOf(className),
                    ClassName = className,
                    Confidence = 1.0f,
                    AnnotationType = "obb",
                    Angle = (float)_rotateRectAngle
                };

                _currentAnnotation.Bboxes.Add(bbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateLabelList();
            }

            _rotateRectPreview = null;
            _rotateRectDirection = null;
        }

        private void HandleEraserClick(Point pos)
        {
            if (_currentRect == null) return;

            BboxCanvas.Children.Remove(_currentRect);
            _currentRect = null;

            // 检查是否点击到某个标注框
            var clickedBbox = _currentAnnotation.Bboxes.FirstOrDefault(bbox =>
            {
                var bboxX = (bbox.X - bbox.Width / 2) * CurrentImage.Width;
                var bboxY = (bbox.Y - bbox.Height / 2) * CurrentImage.Height;
                var bboxWidth = bbox.Width * CurrentImage.Width;
                var bboxHeight = bbox.Height * CurrentImage.Height;
                return pos.X >= bboxX && pos.X <= bboxX + bboxWidth &&
                       pos.Y >= bboxY && pos.Y <= bboxY + bboxHeight;
            });

            if (clickedBbox != null)
            {
                _currentAnnotation.Bboxes.Remove(clickedBbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateLabelList();
                UpdateStatus("标注框已删除");
            }
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            switch (_currentTool)
            {
                case AnnotationTool.Polygon:
                    FinishPolygon();
                    break;
                case AnnotationTool.Keypoints:
                    FinishKeypoints();
                    break;
            }
        }

        private void FinishPolygon()
        {
            if (_polygonPoints.Count < 3)
            {
                UpdateStatus("多边形至少需要3个点");
                ClearPolygonDrawing();
                return;
            }

            // 弹出类别输入对话框
            var classDialog = new ClassSelectDialog(_classes);
            if (classDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(classDialog.SelectedClass))
            {
                var className = classDialog.SelectedClass;

                if (!_classes.Contains(className))
                {
                    _classes.Add(className);
                    SaveClasses();
                    UpdateStatus($"已添加新类别: {className}");
                }

                // 计算多边形的边界框
                var minX = _polygonPoints.Min(p => p.X);
                var minY = _polygonPoints.Min(p => p.Y);
                var maxX = _polygonPoints.Max(p => p.X);
                var maxY = _polygonPoints.Max(p => p.Y);
                var width = maxX - minX;
                var height = maxY - minY;

                // 构建顶点列表（归一化坐标）
                var points = _polygonPoints.Select(p => new List<float>
                {
                    (float)(p.X / CurrentImage.Width),
                    (float)(p.Y / CurrentImage.Height)
                }).ToList();

                var bbox = new BoundingBox
                {
                    X = (float)((minX + width / 2) / CurrentImage.Width),
                    Y = (float)((minY + height / 2) / CurrentImage.Height),
                    Width = (float)(width / CurrentImage.Width),
                    Height = (float)(height / CurrentImage.Height),
                    ClassId = _classes.IndexOf(className),
                    ClassName = className,
                    Confidence = 1.0f,
                    AnnotationType = "polygon",
                    Points = points
                };

                _currentAnnotation.Bboxes.Add(bbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateLabelList();
                UpdateStatus("多边形标注完成");
            }

            ClearPolygonDrawing();
        }

        private void FinishKeypoints()
        {
            if (_keypoints.Count < 1)
            {
                UpdateStatus("关键点至少需要1个点");
                ClearKeypointDrawing();
                return;
            }

            // 弹出类别输入对话框
            var classDialog = new ClassSelectDialog(_classes);
            if (classDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(classDialog.SelectedClass))
            {
                var className = classDialog.SelectedClass;

                if (!_classes.Contains(className))
                {
                    _classes.Add(className);
                    SaveClasses();
                    UpdateStatus($"已添加新类别: {className}");
                }

                // 计算关键点的边界框
                var minX = _keypoints.Min(p => p.X);
                var minY = _keypoints.Min(p => p.Y);
                var maxX = _keypoints.Max(p => p.X);
                var maxY = _keypoints.Max(p => p.Y);
                var width = Math.Max(maxX - minX, 10);
                var height = Math.Max(maxY - minY, 10);

                // 构建关键点列表（归一化坐标）
                var keypoints = _keypoints.Select(p => new List<float>
                {
                    (float)(p.X / CurrentImage.Width),
                    (float)(p.Y / CurrentImage.Height)
                }).ToList();

                var bbox = new BoundingBox
                {
                    X = (float)((minX + width / 2) / CurrentImage.Width),
                    Y = (float)((minY + height / 2) / CurrentImage.Height),
                    Width = (float)(width / CurrentImage.Width),
                    Height = (float)(height / CurrentImage.Height),
                    ClassId = _classes.IndexOf(className),
                    ClassName = className,
                    Confidence = 1.0f,
                    AnnotationType = "keypoints",
                    Keypoints = keypoints
                };

                _currentAnnotation.Bboxes.Add(bbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateLabelList();
                UpdateStatus($"关键点标注完成 ({_keypoints.Count} 个点)");
            }

            ClearKeypointDrawing();
        }

        private void ClearPolygonDrawing()
        {
            foreach (var vertex in _polygonVertices)
            {
                BboxCanvas.Children.Remove(vertex);
            }
            foreach (var line in _polygonLines)
            {
                BboxCanvas.Children.Remove(line);
            }
            _polygonPoints.Clear();
            _polygonVertices.Clear();
            _polygonLines.Clear();
        }

        private void ClearKeypointDrawing()
        {
            foreach (var marker in _keypointMarkers)
            {
                BboxCanvas.Children.Remove(marker);
            }
            _keypoints.Clear();
            _keypointMarkers.Clear();
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    ZoomIn();
                }
                else
                {
                    ZoomOut();
                }
                e.Handled = true;
            }
        }

        private async void BtnUpload_Click(object? sender, RoutedEventArgs? e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                UpdateStatus("正在上传图片...");
                var successCount = 0;

                foreach (var file in dialog.FileNames)
                {
                    if (await _apiService.UploadImageAsync(file))
                    {
                        successCount++;
                    }
                }

                UpdateStatus($"已上传 {successCount}/{dialog.FileNames.Length} 张图片");
                await LoadImagesAsync();
            }
        }

        private async void BtnSave_Click(object? sender, RoutedEventArgs? e)
        {
            if (string.IsNullOrEmpty(_currentImageName))
            {
                MessageBox.Show("请先选择一张图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateStatus("正在保存标注...");
            var success = await _apiService.SaveAnnotationAsync(_currentImageName, _currentAnnotation);

            if (success)
            {
                UpdateStatus("标注保存成功");
            }
            else
            {
                UpdateStatus("标注保存失败");
            }
        }

        private async Task SaveAnnotationInternalAsync()
        {
            if (string.IsNullOrEmpty(_currentImageName))
            {
                return;
            }

            UpdateStatus("正在保存标注...");
            var success = await _apiService.SaveAnnotationAsync(_currentImageName, _currentAnnotation);

            if (success)
            {
                UpdateStatus("标注保存成功");
            }
            else
            {
                UpdateStatus("标注保存失败");
            }
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImageName))
            {
                MessageBox.Show("请先选择一张图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"确定要删除图片 '{_currentImageName}' 吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                UpdateStatus("正在删除图片...");
                var success = await _apiService.DeleteImageAsync(_currentImageName);

                if (success)
                {
                    UpdateStatus("图片删除成功");
                    _currentImageName = string.Empty;
                    CurrentImage.Source = null;
                    BboxCanvas.Children.Clear();

                    // 重新加载图片列表
                    await LoadImagesAsync();

                    // 自动选中下一张图片（或上一张，如果删除的是最后一张）
                    if (LstImages.Items.Count > 0)
                    {
                        LstImages.SelectedIndex = 0; // 选中第一张
                    }

                    MessageBox.Show("图片删除成功！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateStatus("图片删除失败");
                    MessageBox.Show("图片删除失败，请检查后端服务", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnTrain_Click(object sender, RoutedEventArgs e)
        {
            // 检查CUDA
            UpdateStatus("正在检测CUDA...");
            var cudaAvailable = await _apiService.CheckCudaAvailableAsync();
            Console.WriteLine($"[DEBUG] CUDA可用: {cudaAvailable}");

            // 获取详细的CUDA信息
            var cudaInfo = await _apiService.GetCudaInfoAsync();
            Console.WriteLine($"[DEBUG] CUDA信息: Available={cudaInfo?.Available}, DeviceName={cudaInfo?.DeviceName}, DeviceCount={cudaInfo?.DeviceCount}, GpuMemoryGb={cudaInfo?.GpuMemoryGb}, Reason={cudaInfo?.Reason}");

            // 打开训练配置对话框
            var configDialog = new TrainingConfigDialog(
                cudaInfo?.Available ?? false,
                cudaInfo?.DeviceName,
                cudaInfo?.DeviceCount ?? 0,
                cudaInfo?.GpuMemoryGb,
                cudaInfo?.Reason,
                cudaInfo?.PyTorchVersion,
                cudaInfo?.CudaVersion,
                cudaInfo?.DriverVersion,
                cudaInfo?.ComputeCapability,
                cudaInfo?.System,
                cudaInfo?.PythonVersion
            );
            if (configDialog.ShowDialog() != true)
            {
                UpdateStatus("训练已取消");
                return;
            }

            UpdateStatus("正在启动训练...");
            var trainRequest = new TrainRequest
            {
                Epochs = configDialog.Epochs,
                BatchSize = configDialog.BatchSize,
                ImageSize = configDialog.ImageSize,
                Device = configDialog.Device,
                Classes = _classes,
                ModelType = configDialog.ModelType,
                WeightsPath = configDialog.WeightsPath,
                TaskType = "detection"  // 默认使用检测任务
            };

            var success = await _apiService.StartTrainingAsync(trainRequest);

            if (success)
            {
                UpdateStatus("训练已启动，请查看后端日志");
                MessageBox.Show("训练已启动，请查看后端控制台了解训练进度。\n\n训练完成后，请点击'检测识别'按钮测试模型。", "训练启动",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                UpdateStatus("训练启动失败");
            }
        }

        private void BtnDetect_Click(object sender, RoutedEventArgs e)
        {
            var detectionWindow = new DetectionWindow();
            detectionWindow.Owner = this;
            detectionWindow.ShowDialog();
        }

        private void BtnModelManager_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("模型管理功能已移除", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }



        


        private void UpdateStatus(string message)
        {
            StatusBarText.Text = message;
        }

        // 工具按钮事件处理
        private void BtnRectTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.Rectangle;
            UpdateToolButtons();
            UpdateStatus("工具: 矩形框");
        }

        private void BtnPolygonTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.Polygon;
            UpdateToolButtons();
            UpdateStatus("工具: 多边形");
        }

        private void BtnKeypointsTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.Keypoints;
            UpdateToolButtons();
            UpdateStatus("工具: 关键点");
        }

        private void BtnRotateRectTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.RotateRect;
            UpdateToolButtons();
            UpdateStatus("工具: 旋转框");
        }

        private void BtnCircleTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.Circle;
            UpdateToolButtons();
            UpdateStatus("工具: 圆形");
        }

        private void BtnLineTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.Line;
            UpdateToolButtons();
            UpdateStatus("工具: 线段");
        }

        private void BtnPointTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.Point;
            UpdateToolButtons();
            UpdateStatus("工具: 点标注");
        }

        private void BtnPanTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.Pan;
            UpdateToolButtons();
            UpdateStatus("工具: 拖动 - 按住鼠标左键拖动图片");
        }

        private void BtnEdgeAssistTool_Click(object sender, RoutedEventArgs e)
        {
            _currentTool = AnnotationTool.EdgeAssist;
            _edgeAssistEnabled = !_edgeAssistEnabled;

            if (_edgeAssistEnabled)
            {
                UpdateToolButtons();
                UpdateStatus("工具: 边缘检测辅助");
                ShowEdgeOverlay();
            }
            else
            {
                HideEdgeOverlay();
                UpdateToolButtons();
                UpdateStatus("工具: 边缘检测辅助已关闭");
            }
        }

        private void UpdateToolButtons()
        {
            // 更新工具按钮的激活状态
            BtnRectTool.IsChecked = _currentTool == AnnotationTool.Rectangle;
            BtnPolygonTool.IsChecked = _currentTool == AnnotationTool.Polygon;
            BtnKeypointsTool.IsChecked = _currentTool == AnnotationTool.Keypoints;
            BtnRotateRectTool.IsChecked = _currentTool == AnnotationTool.RotateRect;
            BtnCircleTool.IsChecked = _currentTool == AnnotationTool.Circle;
            BtnLineTool.IsChecked = _currentTool == AnnotationTool.Line;
            BtnPointTool.IsChecked = _currentTool == AnnotationTool.Point;
            BtnPanTool.IsChecked = _currentTool == AnnotationTool.Pan;

            var toolName = "";
            switch (_currentTool)
            {
                case AnnotationTool.Rectangle: toolName = "矩形框"; break;
                case AnnotationTool.Polygon: toolName = "多边形"; break;
                case AnnotationTool.Keypoints: toolName = "关键点"; break;
                case AnnotationTool.RotateRect: toolName = "旋转框"; break;
                case AnnotationTool.Circle: toolName = "圆形"; break;
                case AnnotationTool.Line: toolName = "线段"; break;
                case AnnotationTool.Point: toolName = "点"; break;
                case AnnotationTool.Pan: toolName = "拖动"; break;
                case AnnotationTool.EdgeAssist: toolName = "边缘辅助"; break;
            }
            TxtCurrentTool.Text = $"工具: {toolName}";
            StatusBarTool.Text = $"工具: {toolName}";
        }

        // ==================== 边缘检测辅助 ====================

        private void ShowEdgeOverlay()
        {
            if (CurrentImage.Source is BitmapSource bitmapSource)
            {
                // 创建边缘检测图像
                var edgeImage = DetectEdges(bitmapSource);

                if (edgeImage != null)
                {
                    // 创建或更新边缘叠加层
                    if (_edgeOverlayImage == null)
                    {
                        _edgeOverlayImage = new Image
                        {
                            Source = edgeImage,
                            Opacity = 0.5,
                            Stretch = Stretch.Uniform,
                            Width = CurrentImage.ActualWidth,
                            Height = CurrentImage.ActualHeight
                        };
                        Canvas.SetZIndex(_edgeOverlayImage, 5);
                        Canvas.SetLeft(_edgeOverlayImage, 0);
                        Canvas.SetTop(_edgeOverlayImage, 0);
                        AnnotationCanvas.Children.Add(_edgeOverlayImage);
                    }
                    else
                    {
                        _edgeOverlayImage.Source = edgeImage;
                        _edgeOverlayImage.Opacity = 0.5;
                    }
                }
            }
        }

        private void HideEdgeOverlay()
        {
            if (_edgeOverlayImage != null)
            {
                AnnotationCanvas.Children.Remove(_edgeOverlayImage);
                _edgeOverlayImage = null;
            }
        }

        private WriteableBitmap? DetectEdges(BitmapSource source)
        {
            try
            {
                // 转换为 WriteableBitmap 以访问像素数据
                var writeableSource = new WriteableBitmap(source);
                var width = source.PixelWidth;
                var height = source.PixelHeight;
                var stride = writeableSource.BackBufferStride;
                var pixels = new byte[height * stride];

                // 复制像素数据
                writeableSource.CopyPixels(pixels, stride, 0);

                // Sobel 算子
                var sobelX = new double[3, 3]
                {
                    { -1, 0, 1 },
                    { -2, 0, 2 },
                    { -1, 0, 1 }
                };

                var sobelY = new double[3, 3]
                {
                    { -1, -2, -1 },
                    { 0, 0, 0 },
                    { 1, 2, 1 }
                };

                var result = new WriteableBitmap(width, height, 96, 96, source.Format, null);
                var resultPixels = new byte[height * stride];

                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        double gx = 0, gy = 0;
                        for (int ky = -1; ky <= 1; ky++)
                        {
                            for (int kx = -1; kx <= 1; kx++)
                            {
                                int idx = (y + ky) * stride + (x + kx) * 4;
                                double gray = (pixels[idx] + pixels[idx + 1] + pixels[idx + 2]) / 3.0;
                                gx += gray * sobelX[ky + 1, kx + 1];
                                gy += gray * sobelY[ky + 1, kx + 1];
                            }
                        }

                        double magnitude = Math.Sqrt(gx * gx + gy * gy);
                        byte edgePixel = (byte)Math.Max(0, Math.Min(255, magnitude));

                        int idxOut = y * stride + x * 4;
                        resultPixels[idxOut] = edgePixel;
                        resultPixels[idxOut + 1] = edgePixel;
                        resultPixels[idxOut + 2] = edgePixel;
                        resultPixels[idxOut + 3] = 180; // 半透明
                    }
                }

                var rect = new Int32Rect(0, 0, width, height);
                result.WritePixels(rect, resultPixels, stride, 0);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"边缘检测失败: {ex.Message}");
                return null;
            }
        }

        // 导航按钮事件处理
        private void BtnPrevImage_Click(object? sender, RoutedEventArgs? e)
        {
            var currentIndex = LstImages.SelectedIndex;
            if (currentIndex > 0)
            {
                LstImages.SelectedIndex = currentIndex - 1;
            }
        }

        private void BtnNextImage_Click(object? sender, RoutedEventArgs? e)
        {
            var currentIndex = LstImages.SelectedIndex;
            if (currentIndex < LstImages.Items.Count - 1)
            {
                LstImages.SelectedIndex = currentIndex + 1;
            }
        }

        // 缩放按钮事件处理
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _zoomLevel = Math.Min(_zoomLevel + 0.1, 3.0);
            UpdateZoom();
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _zoomLevel = Math.Max(_zoomLevel - 0.1, 0.3);
            UpdateZoom();
        }

        private void BtnZoomReset_Click(object? sender, RoutedEventArgs? e)
        {
            _zoomLevel = 1.0;
            UpdateZoom();
        }

        private void UpdateZoom()
        {
            var scaleTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
            CanvasContainer.RenderTransform = scaleTransform;
            CanvasContainer.RenderTransformOrigin = new Point(0.5, 0.5);
            TxtZoomLevel.Text = $"{(int)(_zoomLevel * 100)}%";
        }

        // 导出按钮事件处理
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            // 创建导出对话框
            var exportDialog = new ExportDialog(_imageNames, _classes);
            if (exportDialog.ShowDialog() == true)
            {
                var exportFormat = exportDialog.SelectedFormat;
                var exportPath = exportDialog.ExportPath;

                if (!string.IsNullOrEmpty(exportPath))
                {
                    ExportDataset(exportFormat, exportPath);
                }
            }
        }
        
        // AI 自动标注按钮事件处理
        private void BtnAutoAnnotation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var autoAnnotationWindow = new AutoAnnotationWindow();
                autoAnnotationWindow.Owner = this;
                autoAnnotationWindow.ShowDialog();
                
                // 自动标注完成后，刷新当前图片的标注
                if (!string.IsNullOrEmpty(_currentImageName))
                {
                    _ = LoadImageAsync(_currentImageName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开自动标注窗口失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ExportDataset(string format, string exportPath)
        {
            try
            {
                UpdateStatus($"正在导出{format}格式数据集...");

                var exportDir = new DirectoryInfo(exportPath);
                if (!exportDir.Exists)
                {
                    exportDir.Create();
                }

                // 创建YOLO目录结构
                var imagesDir = exportDir.CreateSubdirectory("images");
                var labelsDir = exportDir.CreateSubdirectory("labels");

                // 复制图片和标注
                var exportedCount = 0;
                foreach (var imageName in _imageNames)
                {
                    var srcImage = System.IO.Path.Combine(_projectRootDir, "backend", "images", imageName);
                    var srcLabel = System.IO.Path.Combine(_projectRootDir, "backend", "annotations", System.IO.Path.GetFileNameWithoutExtension(imageName) + ".json");

                    if (File.Exists(srcImage))
                    {
                        // 复制图片
                        var destImage = System.IO.Path.Combine(imagesDir.FullName, imageName);
                        File.Copy(srcImage, destImage, true);

                        // 根据格式转换并保存标注
                        if (File.Exists(srcLabel))
                        {
                            var json = File.ReadAllText(srcLabel);
                            var annotation = Newtonsoft.Json.JsonConvert.DeserializeObject<AnnotationData>(json);
                            if (annotation != null)
                            {
                                var destLabel = System.IO.Path.Combine(labelsDir.FullName, System.IO.Path.GetFileNameWithoutExtension(imageName) + ".txt");
                                string yoloLabel = format switch
                                {
                                    "obb" => ConvertToOBBFormat(annotation),
                                    "seg" => ConvertToSegmentationFormat(annotation),
                                    "pose" => ConvertToPoseFormat(annotation),
                                    _ => ConvertToYOLOFormat(annotation)
                                };
                                File.WriteAllText(destLabel, yoloLabel);
                            }
                        }

                        exportedCount++;
                    }
                }

                // 创建data.yaml
                var dataYaml = System.IO.Path.Combine(exportDir.FullName, "data.yaml");
                var yamlContent = $@"path: {exportDir.FullName}
train: images
val: images
nc: {_classes.Count}
names: {Newtonsoft.Json.JsonConvert.SerializeObject(_classes)}";
                File.WriteAllText(dataYaml, yamlContent);

                // 创建classes.txt
                var classesFile = System.IO.Path.Combine(exportDir.FullName, "classes.txt");
                File.WriteAllLines(classesFile, _classes);

                UpdateStatus($"导出完成！共导出 {exportedCount} 张图片到: {exportPath}");
                MessageBox.Show($"数据集导出成功！\n\n共导出 {exportedCount} 张图片\n保存路径: {exportPath}", "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus($"导出失败: {ex.Message}");
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ConvertToYOLOFormat(AnnotationData annotation)
        {
            var lines = new List<string>();
            foreach (var bbox in annotation.Bboxes)
            {
                lines.Add($"{bbox.ClassId} {bbox.X:F6} {bbox.Y:F6} {bbox.Width:F6} {bbox.Height:F6}");
            }
            return string.Join("\n", lines);
        }

        private string ConvertToOBBFormat(AnnotationData annotation)
        {
            var lines = new List<string>();
            foreach (var bbox in annotation.Bboxes)
            {
                if (bbox.AnnotationType == "obb")
                {
                    // YOLO OBB 格式: class_id x_center y_center width height angle
                    // 角度转换为度数 (弧度 -> 度数)
                    var angleDeg = bbox.Angle * 180.0 / Math.PI;
                    lines.Add($"{bbox.ClassId} {bbox.X:F6} {bbox.Y:F6} {bbox.Width:F6} {bbox.Height:F6} {angleDeg:F6}");
                }
                else
                {
                    // 普通边界框转换为 OBB 格式（角度为 0）
                    lines.Add($"{bbox.ClassId} {bbox.X:F6} {bbox.Y:F6} {bbox.Width:F6} {bbox.Height:F6} 0.000000");
                }
            }
            return string.Join("\n", lines);
        }

        private string ConvertToSegmentationFormat(AnnotationData annotation)
        {
            var lines = new List<string>();
            foreach (var bbox in annotation.Bboxes)
            {
                if (bbox.AnnotationType == "polygon" && bbox.Points.Count > 0)
                {
                    // YOLO 分割格式: class_id x1 y1 x2 y2 x3 y3 ...
                    var pointsStr = string.Join(" ", bbox.Points.Select(p => $"{p[0]:F6} {p[1]:F6}"));
                    lines.Add($"{bbox.ClassId} {pointsStr}");
                }
                else
                {
                    // 普通边界框转换为分割格式（使用矩形的 4 个顶点）
                    var x1 = bbox.X - bbox.Width / 2;
                    var y1 = bbox.Y - bbox.Height / 2;
                    var x2 = bbox.X + bbox.Width / 2;
                    var y2 = bbox.Y + bbox.Height / 2;
                    lines.Add($"{bbox.ClassId} {x1:F6} {y1:F6} {x2:F6} {y1:F6} {x2:F6} {y2:F6} {x1:F6} {y2:F6}");
                }
            }
            return string.Join("\n", lines);
        }

        private string ConvertToPoseFormat(AnnotationData annotation)
        {
            var lines = new List<string>();
            foreach (var bbox in annotation.Bboxes)
            {
                if (bbox.AnnotationType == "keypoints" && bbox.Keypoints.Count > 0)
                {
                    // YOLO 姿态估计格式: class_id x1 y1 v1 x2 y2 v2 ...
                    // v 表示可见性（0=不可见, 1=可见, 2=被遮挡）
                    var keypointsStr = string.Join(" ", bbox.Keypoints.Select(p => $"{p[0]:F6} {p[1]:F6} 1"));
                    lines.Add($"{bbox.ClassId} {bbox.X:F6} {bbox.Y:F6} {bbox.Width:F6} {bbox.Height:F6} {keypointsStr}");
                }
                else
                {
                    // 普通边界框转换为姿态估计格式（使用中心点作为关键点）
                    lines.Add($"{bbox.ClassId} {bbox.X:F6} {bbox.Y:F6} {bbox.Width:F6} {bbox.Height:F6} {bbox.X:F6} {bbox.Y:F6} 1");
                }
            }
            return string.Join("\n", lines);
        }

        private void BtnExportOBB_Click(object sender, RoutedEventArgs e)
        {
            var exportDialog = new ExportDialog(_imageNames, _classes);
            exportDialog.SetSelectedFormat("obb");
            if (exportDialog.ShowDialog() == true)
            {
                var exportFormat = exportDialog.SelectedFormat;
                var exportPath = exportDialog.ExportPath;
                if (!string.IsNullOrEmpty(exportPath))
                {
                    ExportDataset(exportFormat, exportPath);
                }
            }
        }

        private void BtnExportSeg_Click(object sender, RoutedEventArgs e)
        {
            var exportDialog = new ExportDialog(_imageNames, _classes);
            exportDialog.SetSelectedFormat("seg");
            if (exportDialog.ShowDialog() == true)
            {
                var exportFormat = exportDialog.SelectedFormat;
                var exportPath = exportDialog.ExportPath;
                if (!string.IsNullOrEmpty(exportPath))
                {
                    ExportDataset(exportFormat, exportPath);
                }
            }
        }

        private void BtnExportPose_Click(object sender, RoutedEventArgs e)
        {
            var exportDialog = new ExportDialog(_imageNames, _classes);
            exportDialog.SetSelectedFormat("pose");
            if (exportDialog.ShowDialog() == true)
            {
                var exportFormat = exportDialog.SelectedFormat;
                var exportPath = exportDialog.ExportPath;
                if (!string.IsNullOrEmpty(exportPath))
                {
                    ExportDataset(exportFormat, exportPath);
                }
            }
        }

        private async void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_imageNames.Count == 0)
            {
                MessageBox.Show("当前没有图片", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"确定要删除所有 {_imageNames.Count} 张图片及其标注吗？\n\n此操作不可撤销！",
                "确认清理",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                UpdateStatus("正在清理所有图片...");
                var successCount = 0;
                var failCount = 0;

                foreach (var imageName in _imageNames.ToList())
                {
                    if (await _apiService.DeleteImageAsync(imageName))
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                // 清空当前显示
                _currentImageName = string.Empty;
                CurrentImage.Source = null;
                BboxCanvas.Children.Clear();
                _currentAnnotation = new AnnotationData();
                UpdateBboxList();

                // 重新加载图片列表
                await LoadImagesAsync();

                UpdateStatus($"清理完成！成功 {successCount} 张，失败 {failCount} 张");
                MessageBox.Show($"清理完成！\n\n成功: {successCount} 张\n失败: {failCount} 张", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnDeleteTrainingData_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要删除所有训练数据吗？\n\n这将删除：\n- 训练好的模型\n- 训练数据集\n\n此操作不可撤销！",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                UpdateStatus("正在删除训练数据...");
                var success = await _apiService.DeleteTrainingDataAsync();

                if (success)
                {
                    UpdateStatus("训练数据删除成功");
                    MessageBox.Show("训练数据删除成功！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateStatus("训练数据删除失败");
                    MessageBox.Show("训练数据删除失败，请检查后端服务", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnVideoDetection_Click(object sender, RoutedEventArgs e)
        {
            var videoWindow = new VideoDetectionWindow();
            videoWindow.Owner = this;
            videoWindow.Show();
        }

        // 标注框管理事件处理
        private void BtnShapeEditor_Click(object sender, RoutedEventArgs e)
        {
            var shapeEditor = new ShapeEditorWindow();
            shapeEditor.Show();
        }

        private async void BtnEditPolygon_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否选中了标注框
            if (_selectedBbox == null)
            {
                MessageBox.Show("请先选择要编辑的多边形标注", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 检查是否是多边形标注
            if (_selectedBbox.Points == null || _selectedBbox.Points.Count < 3)
            {
                MessageBox.Show("选中的标注不是多边形，无法编辑", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 获取当前图片的临时文件路径
                var tempImagePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"temp_{_currentImageName}");
                
                // 如果临时文件不存在，从API获取并保存
                if (!File.Exists(tempImagePath))
                {
                    var base64Image = await _apiService.GetImageBase64Async(_currentImageName);
                    if (!string.IsNullOrEmpty(base64Image))
                    {
                        var imageBytes = Convert.FromBase64String(base64Image);
                        await File.WriteAllBytesAsync(tempImagePath, imageBytes);
                    }
                    else
                    {
                        MessageBox.Show("无法加载图片", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 转换归一化坐标为像素坐标
                var pixelPoints = _selectedBbox.Points.Select(p => new Point(
                    p[0] * CurrentImage.Width,
                    p[1] * CurrentImage.Height
                )).ToList();

                // 打开多边形编辑窗口
                var editWindow = new PolygonEditWindow(pixelPoints, tempImagePath)
                {
                    Owner = this
                };

                if (editWindow.ShowDialog() == true && editWindow.EditedPoints != null)
                {
                    SaveStateForUndo();

                    // 转换回归一化坐标
                    _selectedBbox.Points = editWindow.EditedPoints.Select(p => new List<float>
                    {
                        (float)(p.X / CurrentImage.Width),
                        (float)(p.Y / CurrentImage.Height)
                    }).ToList();

                    // 重新计算边界框
                    var minX = _selectedBbox.Points.Min(p => p[0]);
                    var maxX = _selectedBbox.Points.Max(p => p[0]);
                    var minY = _selectedBbox.Points.Min(p => p[1]);
                    var maxY = _selectedBbox.Points.Max(p => p[1]);

                    _selectedBbox.X = (minX + maxX) / 2;
                    _selectedBbox.Y = (minY + maxY) / 2;
                    _selectedBbox.Width = maxX - minX;
                    _selectedBbox.Height = maxY - minY;

                    DrawBoundingBoxes();
                    UpdateBboxList();
                    UpdateStatus($"多边形已编辑 - 顶点数：{_selectedBbox.Points.Count}");
                }

                // 清理临时文件
                try
                {
                    if (File.Exists(tempImagePath))
                    {
                        File.Delete(tempImagePath);
                    }
                }
                catch
                {
                    // 忽略清理错误
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"编辑多边形失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteBbox_Click(object sender, RoutedEventArgs e)
        {
            if (LstBboxes.SelectedItem == null)
            {
                MessageBox.Show("请先选择要删除的标注框", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveStateForUndo(); // 保存状态以便撤销

            var displayItem = (BoundingBoxDisplayItem)LstBboxes.SelectedItem;
            var bbox = displayItem.Bbox;
            if (_currentAnnotation.Bboxes.Contains(bbox))
            {
                _currentAnnotation.Bboxes.Remove(bbox);
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateStatus("标注框已删除");
            }
        }

        private void BtnApplyBatchEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBboxes.Count < 2)
            {
                MessageBox.Show("请至少选择2个标注框进行批量编辑", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CboBatchClass.SelectedItem == null)
            {
                MessageBox.Show("请选择目标类别", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newClassName = CboBatchClass.SelectedItem.ToString();
            if (string.IsNullOrEmpty(newClassName))
            {
                MessageBox.Show("请选择目标类别", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveStateForUndo();

            foreach (var bbox in _selectedBboxes)
            {
                bbox.ClassName = newClassName;
                bbox.ClassId = _classes.IndexOf(newClassName);
            }

            DrawBoundingBoxes();
            UpdateBboxList();
            UpdateLabelList();
            UpdateStatus($"已将 {_selectedBboxes.Count} 个标注框更改为 '{newClassName}'");
        }

        private void BtnClearBboxes_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnnotation.Bboxes.Count == 0)
            {
                MessageBox.Show("当前没有标注框", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("确定要清空所有标注框吗？", "确认清空", 
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveStateForUndo(); // 保存状态以便撤销
                _currentAnnotation.Bboxes.Clear();
                
                DrawBoundingBoxes();
                UpdateBboxList();
                UpdateStatus("所有标注框已清空");
            }
        }

        // 撤销/重做功能
        private void SaveStateForUndo()
        {
            // 深拷贝当前标注框列表
            var state = _currentAnnotation.Bboxes.Select(b => new BoundingBox
            {
                X = b.X,
                Y = b.Y,
                Width = b.Width,
                Height = b.Height,
                ClassId = b.ClassId,
                ClassName = b.ClassName,
                Confidence = b.Confidence
            }).ToList();

            _undoStack.Push(state);

            // 限制撤销步数
            while (_undoStack.Count > MaxUndoSteps)
            {
                _undoStack.Pop();
            }
        }

        private void Undo()
        {
            if (_undoStack.Count == 0)
            {
                UpdateStatus("没有可撤销的操作");
                return;
            }

            var prevState = _undoStack.Pop();
            _currentAnnotation.Bboxes.Clear();
            _currentAnnotation.Bboxes.AddRange(prevState);

            DrawBoundingBoxes();
            UpdateBboxList();
            UpdateLabelList();
            UpdateStatus($"已撤销 (剩余{_undoStack.Count}步)");
        }

        private void Redo()
        {
            UpdateStatus("重做功能开发中");
        }

        // 更新标注框列表
        private void UpdateBboxList()
        {
            try
            {
                // 创建包装类列表，包含颜色信息
                var bboxDisplayList = _currentAnnotation.Bboxes.Select(bbox => new BoundingBoxDisplayItem
                {
                    Bbox = bbox,
                    ClassColor = GetClassColor(bbox.ClassName)
                }).ToList();

                LstBboxes.ItemsSource = null;
                LstBboxes.ItemsSource = bboxDisplayList;
                TxtCurrentBboxes.Text = $"{_currentAnnotation.Bboxes.Count} 个标注框";

                // 显示/隐藏批量编辑面板
                if (_selectedBboxes.Count >= 2)
                {
                    BatchEditPanel.Visibility = Visibility.Visible;
                    CboBatchClass.ItemsSource = _classes;
                    TxtCurrentBboxes.Text = $"{_currentAnnotation.Bboxes.Count} 个标注框 (已选择 {_selectedBboxes.Count} 个)";
                }
                else
                {
                    BatchEditPanel.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新标注框列表时出错: {ex.Message}");
            }
        }

        // 更新标签列表
        private void UpdateLabelList()
        {
            try
            {
                var labelList = new List<LabelInfo>();

                for (int i = 0; i < _classes.Count; i++)
                {
                    var className = _classes[i];
                    var count = _currentAnnotation.Bboxes.Count(b => b.ClassName == className);
                    labelList.Add(new LabelInfo
                    {
                        Name = className,
                        Color = GetClassColor(className),
                        Count = count
                    });
                }

                LstLabels.ItemsSource = labelList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新标签列表时出错: {ex.Message}");
            }
        }

        // 更新图片列表状态
        private void UpdateImageListStatus()
        {
            var total = _imageNames.Count;
            var annotated = 0;

            if (total > 0)
            {
                var annotationsDir = System.IO.Path.Combine(_projectRootDir, "backend", "annotations");

                // 只计算当前显示的图片数量，避免遍历所有文件
                foreach (var imageName in _imageNames)
                {
                    var jsonPath = System.IO.Path.Combine(annotationsDir, System.IO.Path.GetFileNameWithoutExtension(imageName) + ".json");
                    if (File.Exists(jsonPath))
                    {
                        annotated++;
                    }
                }
            }

            TxtImageCount.Text = $"{total} 个文件";
            TxtAnnotatedCount.Text = $"已标注: {annotated} | 待标注: {total - annotated}";
        }

        // 快捷键系统
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 如果焦点在文本框中，不处理快捷键
            if (Keyboard.FocusedElement is TextBox) return;

            switch (e.Key)
            {
                // 工具切换（有条件修饰符的快捷键优先）
                case Key.R when Keyboard.Modifiers == ModifierKeys.None: // 矩形
                    SelectTool(AnnotationTool.Rectangle);
                    break;
                case Key.P when Keyboard.Modifiers == ModifierKeys.None: // 多边形
                    SelectTool(AnnotationTool.Polygon);
                    break;
                case Key.K when Keyboard.Modifiers == ModifierKeys.None: // 关键点
                    SelectTool(AnnotationTool.Keypoints);
                    break;
                case Key.O when Keyboard.Modifiers == ModifierKeys.None: // 旋转框
                    SelectTool(AnnotationTool.RotateRect);
                    break;
                case Key.E when Keyboard.Modifiers == ModifierKeys.None: // 橡皮擦
                    SelectTool(AnnotationTool.Eraser);
                    break;
                case Key.H when Keyboard.Modifiers == ModifierKeys.None: // 拖动
                    SelectTool(AnnotationTool.Pan);
                    break;

                // 导航快捷键
                case Key.PageUp: // 上一张
                    BtnPrevImage_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.PageDown: // 下一张
                    BtnNextImage_Click(null, null);
                    e.Handled = true;
                    break;

                // 全选快捷键
                case Key.A when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+A 全选
                    SelectAllBboxes();
                    e.Handled = true;
                    break;

                // 缩放快捷键
                case Key.Add:
                case Key.OemPlus:
                    ZoomIn();
                    e.Handled = true;
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    ZoomOut();
                    e.Handled = true;
                    break;
                case Key.D0 when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+0 重置缩放
                    BtnZoomReset_Click(null, null);
                    e.Handled = true;
                    break;

                // 保存快捷键
                case Key.S when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+S 保存
                    BtnSave_Click(null, null);
                    e.Handled = true;
                    break;

                // 撤销/重做
                case Key.Z when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+Z 撤销
                    Undo();
                    e.Handled = true;
                    break;
                case Key.Y when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+Y 重做
                    Redo();
                    e.Handled = true;
                    break;

                // 删除快捷键
                case Key.Delete: // 删除选中的标注框
                case Key.Back: // Backspace删除
                    DeleteSelectedBbox();
                    e.Handled = true;
                    break;

                // 复制粘贴快捷键
                case Key.C when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+C 复制
                    CopySelectedBbox();
                    e.Handled = true;
                    break;
                case Key.V when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+V 粘贴
                    PasteBbox();
                    e.Handled = true;
                    break;

                // 清空标注
                case Key.B when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+B 清空所有标注
                    BtnClearBboxes_Click(null!, null!);
                    e.Handled = true;
                    break;

                // 上传图片
                case Key.U when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+U 上传
                    BtnUpload_Click(null!, null!);
                    e.Handled = true;
                    break;

                // 删除当前图片
                case Key.X when Keyboard.Modifiers == ModifierKeys.Control: // Ctrl+X 删除图片
                    BtnDelete_Click(null!, null!);
                    e.Handled = true;
                    break;

                // Escape键 - 取消绘制
                case Key.Escape:
                    CancelDrawing();
                    e.Handled = true;
                    break;

                // F1 - 帮助
                case Key.F1:
                    ShowHelp();
                    e.Handled = true;
                    break;
            }
        }

        private void CancelDrawing()
        {
            // 取消多边形绘制
            if (_polygonPoints.Count > 0)
            {
                ClearPolygonDrawing();
                UpdateStatus("已取消多边形绘制");
                return;
            }

            // 取消关键点绘制
            if (_keypoints.Count > 0)
            {
                ClearKeypointDrawing();
                UpdateStatus("已取消关键点绘制");
                return;
            }

            // 取消旋转框绘制
            if (_rotateRectPreview != null)
            {
                BboxCanvas.Children.Remove(_rotateRectPreview);
                BboxCanvas.Children.Remove(_rotateRectDirection);
                _rotateRectPreview = null;
                _rotateRectDirection = null;
                UpdateStatus("已取消旋转框绘制");
                return;
            }

            // 取消矩形绘制
            if (_currentRect != null)
            {
                BboxCanvas.Children.Remove(_currentRect);
                _currentRect = null;
                _isDrawing = false;
                UpdateStatus("已取消矩形绘制");
            }
        }

        private void ShowHelp()
        {
            var helpText = @"快捷键说明：

工具切换：
  R - 矩形工具
  P - 多边形工具
  K - 关键点工具
  O - 旋转框工具
  E - 橡皮擦工具

导航：
  A/D - 上一张/下一张图片
  Ctrl+U - 上传图片
  Shift+Delete - 删除当前图片

编辑：
  Ctrl+S - 保存标注
  Ctrl+Z - 撤销
  Ctrl+Y - 重做
  Delete/Backspace - 删除选中标注框
  Ctrl+Delete - 清空所有标注

缩放：
  +/- - 放大/缩小
  Ctrl+0 - 重置缩放

其他：
  Escape - 取消当前绘制
  F1 - 显示帮助";

            MessageBox.Show(helpText, "快捷键帮助", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteSelectedBbox()
        {
            if (_selectedBboxes.Count > 0)
            {
                SaveStateForUndo();
                foreach (var bbox in _selectedBboxes.ToList())
                {
                    _currentAnnotation.Bboxes.Remove(bbox);
                }
                _selectedBboxes.Clear();
                _selectedBbox = null;
                DrawBoundingBoxes();
                            UpdateBboxList();
                            UpdateLabelList();
                            UpdateStatus($"已删除 {_selectedBboxes.Count} 个标注框");
                            }        }

        private void CopySelectedBbox()
        {
            if (_selectedBbox != null)
            {
                // 深度复制标注框
                _copiedBbox = new BoundingBox
                {
                    X = _selectedBbox.X,
                    Y = _selectedBbox.Y,
                    Width = _selectedBbox.Width,
                    Height = _selectedBbox.Height,
                    ClassId = _selectedBbox.ClassId,
                    ClassName = _selectedBbox.ClassName,
                    Confidence = _selectedBbox.Confidence,
                    AnnotationType = _selectedBbox.AnnotationType,
                    Angle = _selectedBbox.Angle,
                    Points = new List<List<float>>(_selectedBbox.Points),
                    Keypoints = new List<List<float>>(_selectedBbox.Keypoints),
                    Color = _selectedBbox.Color
                };
                UpdateStatus("标注框已复制");
            }
        }

        private void PasteBbox()
                {
                    if (_copiedBbox != null)
                    {
                        SaveStateForUndo();
        
                        // 创建新的标注框（稍微偏移一点，避免完全重叠）
                        var newBbox = new BoundingBox
                        {
                            X = Math.Min(1, Math.Max(0, _copiedBbox.X + 0.02f)), // 向右偏移
                            Y = Math.Min(1, Math.Max(0, _copiedBbox.Y + 0.02f)), // 向下偏移
                            Width = _copiedBbox.Width,
                            Height = _copiedBbox.Height,
                            ClassId = _copiedBbox.ClassId,
                            ClassName = _copiedBbox.ClassName,
                            Confidence = _copiedBbox.Confidence,
                            AnnotationType = _copiedBbox.AnnotationType,
                            Angle = _copiedBbox.Angle,
                            Points = new List<List<float>>(_copiedBbox.Points),
                            Keypoints = new List<List<float>>(_copiedBbox.Keypoints),
                            Color = _copiedBbox.Color
                        };
        
                        _currentAnnotation.Bboxes.Add(newBbox);
                        _selectedBbox = newBbox;
        
                        DrawBoundingBoxes();
                        UpdateBboxList();
                        UpdateLabelList();
                        UpdateStatus("标注框已粘贴");
                    }
                }
        
                private void SelectAllBboxes()
                        {
                            if (_currentAnnotation.Bboxes.Count > 0)
                            {
                                _selectedBboxes.Clear();
                                _selectedBboxes.AddRange(_currentAnnotation.Bboxes);
                                _selectedBbox = _selectedBboxes.Count > 0 ? _selectedBboxes[0] : null;
                                DrawBoundingBoxes();
                                UpdateBboxList();
                                UpdateStatus($"已全选 {_selectedBboxes.Count} 个标注框");
                            }
                        }
                        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
                
                                {
                
                                    if (sender is Ellipse handle && handle.Tag is int handleIndex)
                
                                    {
                
                                        _isResizing = true;
                
                                        _resizeHandleIndex = handleIndex;
                
                                        _resizeStartPos = e.GetPosition(AnnotationCanvas);
                
                                        _originalWidth = _selectedBbox?.Width * CurrentImage.Width ?? 0;
                
                                        _originalHeight = _selectedBbox?.Height * CurrentImage.Height ?? 0;
                
                                        _originalCenter = new Point(_selectedBbox?.X * CurrentImage.Width ?? 0, _selectedBbox?.Y * CurrentImage.Height ?? 0);
                
                                        
                
                                        e.Handled = true;
                
                                    }
                
                                }
                
                        
                
                                private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
                
                                {
                
                                    if (!_isResizing || _selectedBbox == null) return;
                
                        
                
                                    var currentPos = e.GetPosition(AnnotationCanvas);
                
                                    var deltaX = currentPos.X - _resizeStartPos.X;
                
                                    var deltaY = currentPos.Y - _resizeStartPos.Y;
                
                        
                
                                    // 根据手柄索引调整大小
                
                                    switch (_resizeHandleIndex)
                
                                    {
                
                                        case 0: // 左上
                
                                            _selectedBbox.X = (float)((_originalCenter.X + deltaX + _originalWidth / 2) / CurrentImage.Width);
                
                                            _selectedBbox.Y = (float)((_originalCenter.Y + deltaY + _originalHeight / 2) / CurrentImage.Height);
                
                                            _selectedBbox.Width = (float)(_originalWidth - deltaX / CurrentImage.Width);
                
                                            _selectedBbox.Height = (float)(_originalHeight - deltaY / CurrentImage.Height);
                
                                            break;
                
                                        case 1: // 上中
                
                                            _selectedBbox.Y = (float)((_originalCenter.Y + deltaY + _originalHeight / 2) / CurrentImage.Height);
                
                                            _selectedBbox.Height = (float)(_originalHeight - deltaY / CurrentImage.Height);
                
                                            break;
                
                                        case 2: // 右上
                
                                            _selectedBbox.Y = (float)((_originalCenter.Y + deltaY + _originalHeight / 2) / CurrentImage.Height);
                
                                            _selectedBbox.Width = (float)(_originalWidth + deltaX / CurrentImage.Width);
                
                                            _selectedBbox.Height = (float)(_originalHeight - deltaY / CurrentImage.Height);
                
                                            break;
                
                                        case 3: // 右中
                
                                            _selectedBbox.Width = (float)(_originalWidth + deltaX / CurrentImage.Width);
                
                                            break;
                
                                        case 4: // 右下
                
                                            _selectedBbox.Width = (float)(_originalWidth + deltaX / CurrentImage.Width);
                
                                            _selectedBbox.Height = (float)(_originalHeight + deltaY / CurrentImage.Height);
                
                                            break;
                
                                        case 5: // 下中
                
                                            _selectedBbox.Height = (float)(_originalHeight + deltaY / CurrentImage.Height);
                
                                            break;
                
                                        case 6: // 左下
                
                                            _selectedBbox.X = (float)((_originalCenter.X + deltaX + _originalWidth / 2) / CurrentImage.Width);
                
                                            _selectedBbox.Width = (float)(_originalWidth - deltaX / CurrentImage.Width);
                
                                            _selectedBbox.Height = (float)(_originalHeight + deltaY / CurrentImage.Height);
                
                                            break;
                
                                        case 7: // 左中
                
                                            _selectedBbox.X = (float)((_originalCenter.X + deltaX + _originalWidth / 2) / CurrentImage.Width);
                
                                            _selectedBbox.Width = (float)(_originalWidth - deltaX / CurrentImage.Width);
                
                                            break;
                
                                    }
                
                        
                
                                    // 确保最小尺寸
                
                                    _selectedBbox.Width = Math.Max(0.01f, _selectedBbox.Width);
                
                                    _selectedBbox.Height = Math.Max(0.01f, _selectedBbox.Height);
                
                        
                
                                    DrawBoundingBoxes();
                
                                }
                
                        
                
                                private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
                
                                {
                
                                    if (_isResizing)
                
                                    {
                
                                        _isResizing = false;
                
                                        _resizeHandleIndex = -1;
                
                                        UpdateBboxList();
                
                                                                                
                
                                                                            }
                
                                                                        
                
                                                                        }        private void SelectTool(AnnotationTool tool)
        {
            _currentTool = tool;
            UpdateToolButtons();

            var toolName = "";
            switch (tool)
            {
                case AnnotationTool.Rectangle: toolName = "矩形框"; break;
                case AnnotationTool.Polygon: toolName = "多边形"; break;
                case AnnotationTool.Keypoints: toolName = "关键点"; break;
                case AnnotationTool.RotateRect: toolName = "旋转框"; break;
                case AnnotationTool.Pan: toolName = "拖动"; break;
                case AnnotationTool.Eraser: toolName = "橡皮擦"; break;
            }
            UpdateStatus($"工具: {toolName}");
        }

        private void ZoomIn()
        {
            _zoomLevel = Math.Min(_zoomLevel + 0.1, 3.0);
            UpdateZoom();
        }

        private void ZoomOut()
        {
            _zoomLevel = Math.Max(_zoomLevel - 0.1, 0.3);
            UpdateZoom();
        }

        // 更新标注框列表
        private async void BtnDeleteAll_Click(object sender, RoutedEventArgs e)
        {
            if (_imageNames == null || _imageNames.Count == 0)
            {
                MessageBox.Show("没有图片可以删除", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"确定要删除所有 {_imageNames.Count} 张图片吗？\n\n此操作不可撤销！",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                UpdateStatus("正在删除所有图片...");
                var successCount = 0;
                var failCount = 0;

                foreach (var imageName in _imageNames.ToList())
                {
                    if (await _apiService.DeleteImageAsync(imageName))
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                UpdateStatus($"删除完成：成功 {successCount} 张，失败 {failCount} 张");
                _currentImageName = string.Empty;
                CurrentImage.Source = null;
                BboxCanvas.Children.Clear();
                await LoadImagesAsync();
            }
        }

        private async void BtnDeleteTraining_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "确定要删除所有训练数据吗？\n\n这将删除：\n- 所有训练好的模型\n- 数据集文件\n\n此操作不可撤销！",
                "确认删除训练数据",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                UpdateStatus("正在删除训练数据...");
                var success = await _apiService.DeleteTrainingDataAsync();

                if (success)
                {
                    UpdateStatus("训练数据删除成功");
                    MessageBox.Show("训练数据已成功删除。如果需要重新训练，请先标注图片，然后点击'训练模型'。", "删除成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateStatus("训练数据删除失败");
                    MessageBox.Show("删除训练数据失败，请检查后端日志。", "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
                private void BtnAdvancedDetect_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否有当前图片
            if (string.IsNullOrEmpty(_currentImageName))
            {
                MessageBox.Show("请先打开一张图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 打开高级检测窗口
            var advancedDetectWindow = new AdvancedDetectionWindow(_currentImageName);
            advancedDetectWindow.Owner = this;
            advancedDetectWindow.ShowDialog();

            // 如果应用了检测结果，重新加载
            if (advancedDetectWindow.DialogResult == true)
            {
                _ = LoadImageAsync(_currentImageName);
            }
        }

        // ==================== 标注类型转换功能 ====================

        private void ConvertAnnotationType(BoundingBox bbox, string newType)
        {
            if (bbox == null) return;

            switch (newType.ToLower())
            {
                case "rectangle":
                case "bbox":
                    ConvertToRectangle(bbox);
                    break;
                case "polygon":
                    ConvertToPolygon(bbox);
                    break;
            }
        }

        private void ConvertToRectangle(BoundingBox bbox)
        {
            switch (bbox.AnnotationType.ToLower())
            {
                case "polygon":
                    if (bbox.Points.Count >= 2)
                    {
                        var minX = bbox.Points.Min(p => p[0]);
                        var maxX = bbox.Points.Max(p => p[0]);
                        var minY = bbox.Points.Min(p => p[1]);
                        var maxY = bbox.Points.Max(p => p[1]);
                        bbox.X = (minX + maxX) / 2;
                        bbox.Y = (minY + maxY) / 2;
                        bbox.Width = maxX - minX;
                        bbox.Height = maxY - minY;
                        bbox.Points = new List<List<float>>();
                    }
                    break;
            }

            bbox.AnnotationType = "bbox";
        }

        private void ConvertToPolygon(BoundingBox bbox)
        {
            switch (bbox.AnnotationType.ToLower())
            {
                case "rectangle":
                case "bbox":
                    var halfW = bbox.Width / 2;
                    var halfH = bbox.Height / 2;
                    bbox.Points = new List<List<float>>
                    {
                        new List<float> { bbox.X - halfW, bbox.Y - halfH },
                        new List<float> { bbox.X + halfW, bbox.Y - halfH },
                        new List<float> { bbox.X + halfW, bbox.Y + halfH },
                        new List<float> { bbox.X - halfW, bbox.Y + halfH }
                    };
                    break;
            }

            bbox.AnnotationType = "polygon";
        }

        // ==================== 标注裁剪保存功能 ====================

        private void CropAndSaveAnnotation(BoundingBox bbox)
        {
            if (bbox == null || CurrentImage == null) return;

            try
            {
                if (CurrentImage.Source is not BitmapSource bitmapSource) return;

                var imgWidth = bitmapSource.PixelWidth;
                var imgHeight = bitmapSource.PixelHeight;

                Rect cropRect;
                string filename;

                switch (bbox.AnnotationType.ToLower())
                {
                    case "rectangle":
                    case "bbox":
                        var x1 = (bbox.X - bbox.Width / 2) * imgWidth;
                        var y1 = (bbox.Y - bbox.Height / 2) * imgHeight;
                        var x2 = (bbox.X + bbox.Width / 2) * imgWidth;
                        var y2 = (bbox.Y + bbox.Height / 2) * imgHeight;
                        cropRect = new Rect(x1, y1, x2 - x1, y2 - y1);
                        filename = $"crop_{bbox.ClassName}_{Guid.NewGuid():N}.png";
                        break;

                    case "polygon":
                        if (bbox.Points.Count < 3)
                        {
                            MessageBox.Show("多边形点数不足，无法裁剪", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        var minX = bbox.Points.Min(p => p[0]) * imgWidth;
                        var maxX = bbox.Points.Max(p => p[0]) * imgWidth;
                        var minY = bbox.Points.Min(p => p[1]) * imgHeight;
                        var maxY = bbox.Points.Max(p => p[1]) * imgHeight;
                        cropRect = new Rect(minX, minY, maxX - minX, maxY - minY);
                        filename = $"crop_{bbox.ClassName}_{Guid.NewGuid():N}.png";
                        break;

                    default:
                        MessageBox.Show("不支持的标注类型", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                }

                cropRect.Intersect(new Rect(0, 0, imgWidth, imgHeight));

                if (cropRect.Width <= 0 || cropRect.Height <= 0)
                {
                    MessageBox.Show("裁剪区域无效", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var croppedBitmap = new CroppedBitmap(bitmapSource, new Int32Rect(
                    (int)cropRect.X,
                    (int)cropRect.Y,
                    (int)cropRect.Width,
                    (int)cropRect.Height
                ));

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(croppedBitmap));

                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg)|*.jpg|所有文件 (*.*)|*.*",
                    DefaultExt = "png",
                    FileName = filename
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    using (var stream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                    {
                        encoder.Save(stream);
                    }

                    MessageBox.Show($"裁剪图片已保存到：{saveFileDialog.FileName}", "成功",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"裁剪保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class ClassManagerDialog : Window
    {
        private readonly List<string> _classes;
        private readonly Action<List<string>> _onClassesChanged;
        private readonly Action<string>? _onClassDeleted;
        private TextBlock _statusText;

        public ClassManagerDialog(List<string> classes, Action<List<string>> onClassesChanged, Action<string>? onClassDeleted = null)
        {
            _classes = classes;
            _onClassesChanged = onClassesChanged;
            _onClassDeleted = onClassDeleted;

            Title = "类别管理";
            Width = 520;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            Background = (SolidColorBrush)Application.Current.Resources["BackgroundBrush"];
            Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });  // 自定义标题栏
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题区
            var titleLabel = new TextBlock
            {
                Text = "标注类别管理",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                Margin = new Thickness(20, 20, 20, 15),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            grid.Children.Add(titleLabel);
            Grid.SetRow(titleLabel, 1);

            // 类别列表
            var listBorder = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["SurfaceBrush"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"],
                BorderThickness = new Thickness(1),
                Margin = new Thickness(20, 0, 20, 15),
                Padding = new Thickness(5)
            };

            var listBox = new ListBox
            {
                Name = "ClassListBox",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"]
            };

            foreach (var className in _classes)
            {
                listBox.Items.Add(className);
            }

            listBorder.Child = listBox;
            grid.Children.Add(listBorder);
            Grid.SetRow(listBorder, 2);

            // 按钮区 - 上方操作按钮
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(20, 0, 0, 10)
            };

            var btnAdd = new Button
            {
                Content = "添加",
                Width = 80,
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 8, 16, 8)
            };
            btnAdd.Click += (s, e) =>
            {
                var inputDialog = new InputDialog("添加类别", "请输入新类别名称：");
                if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
                {
                    var newClass = inputDialog.InputText.Trim();
                    if (!_classes.Contains(newClass))
                    {
                        _classes.Add(newClass);
                        listBox.Items.Add(newClass);
                        UpdateStatusText();
                    }
                    else
                    {
                        MessageBox.Show("该类别已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            var btnEdit = new Button
            {
                Content = "编辑",
                Width = 80,
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 8, 16, 8)
            };
            btnEdit.Click += (s, e) =>
            {
                if (listBox.SelectedItem == null)
                {
                    MessageBox.Show("请先选择要编辑的类别", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var oldClass = listBox.SelectedItem.ToString()!;
                var inputDialog = new InputDialog("编辑类别", "请输入新的类别名称：");
                inputDialog.InputText = oldClass;
                if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.InputText))
                {
                    var newClass = inputDialog.InputText.Trim();
                    if (!_classes.Contains(newClass) || newClass == oldClass)
                    {
                        var index = _classes.IndexOf(oldClass);
                        _classes[index] = newClass;
                        listBox.Items[index] = newClass;
                        UpdateStatusText();
                    }
                    else
                    {
                        MessageBox.Show("该类别已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            var btnDelete = new Button
            {
                Content = "删除",
                Width = 80,
                Height = 36,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 8, 16, 8)
            };
            btnDelete.Click += (s, e) =>
            {
                if (listBox.SelectedItem == null)
                {
                    MessageBox.Show("请先选择要删除的类别", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_classes.Count <= 1)
                {
                    MessageBox.Show("至少需要保留一个类别", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var className = listBox.SelectedItem.ToString()!;
                var result = MessageBox.Show($"确定要删除类别 '{className}' 吗？", "确认删除",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _classes.Remove(className!);
                    listBox.Items.Remove(className!);

                    // 通知调用者类别已被删除，以便进行清理操作
                    _onClassDeleted?.Invoke(className!);

                    UpdateStatusText();
                }
            };

            // 导入按钮
            var btnImport = new Button
            {
                Content = "导入",
                Width = 80,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 8, 16, 8)
            };
            btnImport.Click += (s, e) =>
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "导入类别",
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        var filePath = openFileDialog.FileName;
                        var importedClasses = System.IO.File.ReadAllLines(filePath)
                            .Where(line => !string.IsNullOrWhiteSpace(line))
                            .Select(line => line.Trim())
                            .ToList();

                        if (importedClasses.Count > 0)
                        {
                            var addedCount = 0;
                            foreach (var cls in importedClasses)
                            {
                                if (!_classes.Contains(cls))
                                {
                                    _classes.Add(cls);
                                    listBox.Items.Add(cls);
                                    addedCount++;
                                }
                            }
                            UpdateStatusText();
                            MessageBox.Show($"成功导入 {addedCount} 个新类别！", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("文件中没有找到有效的类别", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            // 导出按钮
            var btnExport = new Button
            {
                Content = "导出",
                Width = 80,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 8, 16, 8)
            };
            btnExport.Click += (s, e) =>
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "导出类别",
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FileName = "classes.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    try
                    {
                        System.IO.File.WriteAllLines(saveFileDialog.FileName, _classes);
                        MessageBox.Show($"成功导出 {_classes.Count} 个类别！", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            };

            var btnClose = new Button
            {
                Content = "关闭",
                Width = 80,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(55, 65, 81)),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Cursor = Cursors.Hand,
                Padding = new Thickness(16, 8, 16, 8)
            };
            btnClose.Click += (s, e) =>
            {
                _onClassesChanged(_classes);
                DialogResult = true;
            };

            buttonPanel.Children.Add(btnAdd);
            buttonPanel.Children.Add(btnEdit);
            buttonPanel.Children.Add(btnDelete);
            buttonPanel.Children.Add(btnImport);
            buttonPanel.Children.Add(btnExport);
            buttonPanel.Children.Add(btnClose);

            // 状态文本
            _statusText = new TextBlock
            {
                Name = "StatusText",
                Text = $"当前共有 {_classes.Count} 个类别",
                Foreground = (SolidColorBrush)Application.Current.Resources["TextSecondaryBrush"],
                FontSize = 11,
                Margin = new Thickness(20, 0, 0, 5),
                VerticalAlignment = VerticalAlignment.Center
            };

            buttonPanel.Children.Add(_statusText);

            grid.Children.Add(buttonPanel);
            Grid.SetRow(buttonPanel, 3);

            // 自定义标题栏
            var titleBarBorder = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["TitleBarBrush"],
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var titleBarGrid = new Grid();
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "类别管理",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0)
            };
            Grid.SetColumn(titleText, 0);
            titleBarGrid.Children.Add(titleText);

            // 关闭按钮
            var btnCloseTitle = new Button
            {
                Content = "✕",
                Width = 46,
                Height = 32,
                Background = Brushes.Transparent,
                Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"],
                BorderThickness = new Thickness(0),
                FontSize = 16,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            btnCloseTitle.Click += (s, e) =>
            {
                _onClassesChanged(_classes);
                DialogResult = true;
            };
            btnCloseTitle.MouseEnter += (s, e) => btnCloseTitle.Background = new SolidColorBrush(Color.FromRgb(232, 17, 35));
            btnCloseTitle.MouseEnter += (s, e) => btnCloseTitle.Foreground = Brushes.White;
            btnCloseTitle.MouseLeave += (s, e) => btnCloseTitle.Background = Brushes.Transparent;
            btnCloseTitle.MouseLeave += (s, e) => btnCloseTitle.Foreground = (SolidColorBrush)Application.Current.Resources["TextPrimaryBrush"];
            Grid.SetColumn(btnCloseTitle, 1);
            titleBarGrid.Children.Add(btnCloseTitle);

            titleBarBorder.Child = titleBarGrid;
            grid.Children.Add(titleBarBorder);
            Grid.SetRow(titleBarBorder, 0);

            Content = grid;
        }

        private void UpdateStatusText()
        {
            if (_statusText != null)
            {
                _statusText.Text = $"当前共有 {_classes.Count} 个类别";
            }
        }
    }

    public class InputDialog : Window
    {
        public string InputText { get; set; } = string.Empty;

        public InputDialog(string title, string prompt)
        {
            Title = title;
            Width = 420;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
            Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229));
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.SingleBorderWindow;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题区
            var titlePanel = new StackPanel
            {
                Margin = new Thickness(0, 15, 0, 8)
            };

            var titleLabel = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titlePanel.Children.Add(titleLabel);

            // 提示文本
            var promptTextBlock = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(30, 0, 30, 12),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(153, 153, 153)),
                TextWrapping = TextWrapping.Wrap
            };

            // 输入框
            var textBox = new TextBox
            {
                Margin = new Thickness(30, 0, 30, 15),
                Padding = new Thickness(10, 6, 10, 6),
                Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                BorderThickness = new Thickness(1),
                FontSize = 13,
                Height = 36,
                VerticalContentAlignment = VerticalAlignment.Center,
                CaretBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229))
            };
            textBox.TextChanged += (s, e) => InputText = textBox.Text;

            // 按钮区
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 30, 20)
            };

            var btnOk = new Button
            {
                Content = "确定",
                Width = 100,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0)
            };
            btnOk.Click += (s, e) => DialogResult = true;

            var btnCancel = new Button
            {
                Content = "取消",
                Width = 100,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(77, 77, 77)),
                Foreground = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0)
            };
            btnCancel.Click += (s, e) => DialogResult = false;

            buttonPanel.Children.Add(btnOk);
            buttonPanel.Children.Add(btnCancel);

            grid.Children.Add(titlePanel);
            grid.Children.Add(promptTextBlock);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            Grid.SetRow(titlePanel, 0);
            Grid.SetRow(promptTextBlock, 1);
            Grid.SetRow(textBox, 2);
            Grid.SetRow(buttonPanel, 3);

            Content = grid;

            this.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) DialogResult = true;
                if (e.Key == Key.Escape) DialogResult = false;
            };
        }
    }

    // 辅助类
    public class ImageFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public bool IsAnnotated { get; set; }
    }

    public class LabelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#22c55e";
        public int Count { get; set; }
    }

    public class BoundingBoxDisplayItem
    {
        public BoundingBox Bbox { get; set; } = null!;
        public string ClassColor { get; set; } = "#22c55e";
    }
}