using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using YoloAnnotator.Models;
using YoloAnnotator.Services;

namespace YoloAnnotator
{
    public partial class ShapeEditorWindow : Window
    {
        private readonly ApiService _apiService;
        private ObservableCollection<Models.Shape> _shapes;
        private string? _currentImageName;
        private BitmapImage? _currentImage;
        
        // 绘制状�?
        private enum DrawMode { None, Rectangle, Rotation, Polygon, Circle, Line, Point }
        private DrawMode _currentMode = DrawMode.Rectangle;
        private bool _isDrawing = false;
        private Point _startPoint;
        private List<Point> _polygonPoints = new List<Point>();
        private System.Windows.Shapes.Shape? _tempShape;
        private Models.Shape? _selectedShape;

        public ShapeEditorWindow()
        {
            InitializeComponent();
            _apiService = new ApiService();
            _shapes = new ObservableCollection<Models.Shape>();
            ShapesList.ItemsSource = _shapes;
        }

        // 安全地更新状态栏
        private void UpdateStatus(string message)
        {
            if (TxtStatus != null)
            {
                TxtStatus.Text = message;
            }
        }

        // ==================== 工具选择 ====================
        
        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            if (sender == RbRectangle) _currentMode = DrawMode.Rectangle;
            else if (sender == RbRotation) _currentMode = DrawMode.Rotation;
            else if (sender == RbPolygon) _currentMode = DrawMode.Polygon;
            else if (sender == RbCircle) _currentMode = DrawMode.Circle;
            else if (sender == RbLine) _currentMode = DrawMode.Line;
            else if (sender == RbPoint) _currentMode = DrawMode.Point;
            
            UpdateStatus($"当前工具: {_currentMode}");
        }

        // ==================== 图片加载 ====================
        
        private async void BtnLoadImage_Click(object sender, RoutedEventArgs e)
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
                    ImageDisplay.Source = _currentImage;
                    MainCanvas.Width = _currentImage.PixelWidth;
                    MainCanvas.Height = _currentImage.PixelHeight;
                    
                    _currentImageName = System.IO.Path.GetFileName(dialog.FileName);
                    TxtImageName.Text = _currentImageName;
                    
                    // 加载已有标注
                    await LoadAnnotations();
                    
                    UpdateStatus($"已加载图片: {_currentImageName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"加载图片失败: {ex.Message}", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async System.Threading.Tasks.Task LoadAnnotations()
        {
            if (string.IsNullOrEmpty(_currentImageName)) return;

            try
            {
                var annotation = await _apiService.GetAnnotationAsync(_currentImageName);
                if (annotation?.Bboxes != null)
                {
                    _shapes.Clear();
                    foreach (var bbox in annotation.Bboxes)
                    {
                        // 转换为Shape对象
                        var shape = ConvertBBoxToShape(bbox);
                        if (shape != null)
                        {
                            _shapes.Add(shape);
                        }
                    }
                    RedrawAllShapes();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载标注失败: {ex.Message}");
            }
        }

        // ==================== 鼠标事件处理 ====================
        
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentImage == null) return;

            var pos = e.GetPosition(MainCanvas);
            
            switch (_currentMode)
            {
                case DrawMode.Rectangle:
                case DrawMode.Rotation:
                case DrawMode.Circle:
                case DrawMode.Line:
                    _isDrawing = true;
                    _startPoint = pos;
                    break;
                    
                case DrawMode.Polygon:
                    _polygonPoints.Add(pos);
                    DrawTempPolygon();
                    break;
                    
                case DrawMode.Point:
                    CreatePointShape(pos);
                    break;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || _currentImage == null) return;

            var pos = e.GetPosition(MainCanvas);
            
            // 移除临时形状
            if (_tempShape != null)
            {
                MainCanvas.Children.Remove(_tempShape);
                _tempShape = null;
            }

            // 绘制临时形状
            switch (_currentMode)
            {
                case DrawMode.Rectangle:
                    _tempShape = CreateTempRectangle(_startPoint, pos);
                    break;
                case DrawMode.Circle:
                    _tempShape = CreateTempCircle(_startPoint, pos);
                    break;
                case DrawMode.Line:
                    _tempShape = CreateTempLine(_startPoint, pos);
                    break;
            }

            if (_tempShape != null)
            {
                MainCanvas.Children.Add(_tempShape);
            }

            // 更新状态栏
            UpdateStatus($"位置: ({pos.X:F0}, {pos.Y:F0})");
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing || _currentImage == null) return;

            var pos = e.GetPosition(MainCanvas);
            
            // 移除临时形状
            if (_tempShape != null)
            {
                MainCanvas.Children.Remove(_tempShape);
                _tempShape = null;
            }

            // 创建最终形状
            switch (_currentMode)
            {
                case DrawMode.Rectangle:
                    CreateRectangleShape(_startPoint, pos);
                    break;
                case DrawMode.Circle:
                    CreateCircleShape(_startPoint, pos);
                    break;
                case DrawMode.Line:
                    CreateLineShape(_startPoint, pos);
                    break;
            }

            _isDrawing = false;
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 右键完成多边形绘制
            if (_currentMode == DrawMode.Polygon && _polygonPoints.Count >= 3)
            {
                CreatePolygonShape();
                _polygonPoints.Clear();
                
                // 清除临时多边形
                var tempPolygons = MainCanvas.Children.OfType<Polygon>()
                    .Where(p => p.Tag?.ToString() == "temp").ToList();
                foreach (var p in tempPolygons)
                {
                    MainCanvas.Children.Remove(p);
                }
            }
        }

        // ==================== 形状创建 ====================
        
        private void CreateRectangleShape(Point start, Point end)
        {
            var label = PromptForLabel();
            if (string.IsNullOrEmpty(label)) return;

            var shape = Models.Shape.CreateRectangle(
                (float)Math.Min(start.X, end.X),
                (float)Math.Min(start.Y, end.Y),
                (float)Math.Max(start.X, end.X),
                (float)Math.Max(start.Y, end.Y),
                label
            );

            _shapes.Add(shape);
            RedrawAllShapes();
            UpdateStatus($"已添加矩形: {label}");
        }

        private void CreateCircleShape(Point center, Point edge)
        {
            var label = PromptForLabel();
            if (string.IsNullOrEmpty(label)) return;

            var radius = Math.Sqrt(Math.Pow(edge.X - center.X, 2) + Math.Pow(edge.Y - center.Y, 2));
            
            var shape = Models.Shape.CreateCircle(
                (float)center.X,
                (float)center.Y,
                (float)radius,
                label
            );

            _shapes.Add(shape);
            RedrawAllShapes();
            UpdateStatus($"已添加圆形: {label}");
        }

        private void CreateLineShape(Point start, Point end)
        {
            var label = PromptForLabel();
            if (string.IsNullOrEmpty(label)) return;

            var shape = Models.Shape.CreateLine(
                (float)start.X,
                (float)start.Y,
                (float)end.X,
                (float)end.Y,
                label
            );

            _shapes.Add(shape);
            RedrawAllShapes();
            UpdateStatus($"已添加线段: {label}");
        }

        private void CreatePointShape(Point pos)
        {
            var label = PromptForLabel();
            if (string.IsNullOrEmpty(label)) return;

            var shape = Models.Shape.CreatePoint(
                (float)pos.X,
                (float)pos.Y,
                label
            );

            _shapes.Add(shape);
            RedrawAllShapes();
            UpdateStatus($"已添加点: {label}");
        }

        private void CreatePolygonShape()
        {
            var label = PromptForLabel();
            if (string.IsNullOrEmpty(label)) return;

            var points = _polygonPoints.Select(p => new List<float> { (float)p.X, (float)p.Y }).ToList();
            var shape = Models.Shape.CreatePolygon(points, label);

            _shapes.Add(shape);
            RedrawAllShapes();
            UpdateStatus($"已添加多边形: {label} ({points.Count} 个点)");
        }

        // ==================== 临时形状绘制 ====================
        
        private System.Windows.Shapes.Rectangle CreateTempRectangle(Point start, Point end)
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                Fill = Brushes.Transparent
            };

            Canvas.SetLeft(rect, Math.Min(start.X, end.X));
            Canvas.SetTop(rect, Math.Min(start.Y, end.Y));
            rect.Width = Math.Abs(end.X - start.X);
            rect.Height = Math.Abs(end.Y - start.Y);

            return rect;
        }

        private Ellipse CreateTempCircle(Point center, Point edge)
        {
            var radius = Math.Sqrt(Math.Pow(edge.X - center.X, 2) + Math.Pow(edge.Y - center.Y, 2));
            
            var ellipse = new Ellipse
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                Fill = Brushes.Transparent,
                Width = radius * 2,
                Height = radius * 2,
                Stretch = Stretch.Uniform
            };

            Canvas.SetLeft(ellipse, center.X - radius);
            Canvas.SetTop(ellipse, center.Y - radius);

            return ellipse;
        }

        private Line CreateTempLine(Point start, Point end)
        {
            return new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 3 }
            };
        }

        private void DrawTempPolygon()
        {
            // 清除旧的临时多边形
            var tempPolygons = MainCanvas.Children.OfType<Polygon>()
                .Where(p => p.Tag?.ToString() == "temp").ToList();
            foreach (var p in tempPolygons)
            {
                MainCanvas.Children.Remove(p);
            }

            if (_polygonPoints.Count < 2) return;

            var polygon = new Polygon
            {
                Stroke = Brushes.Yellow,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 3 },
                Fill = Brushes.Transparent,
                Points = new PointCollection(_polygonPoints),
                Tag = "temp"
            };

            MainCanvas.Children.Add(polygon);
        }

        // ==================== 形状渲染 ====================
        
        private void RedrawAllShapes()
        {
            // 如果画布还没初始化，直接返回
            if (MainCanvas == null || ImageDisplay == null)
            {
                return;
            }

            // 清除所有形状（保留图片）
            var shapesToRemove = MainCanvas.Children.OfType<UIElement>()
                .Where(e => e != ImageDisplay).ToList();
            foreach (var shape in shapesToRemove)
            {
                MainCanvas.Children.Remove(shape);
            }

            // 重新绘制所有形状
            foreach (var shape in _shapes)
            {
                DrawShape(shape);
            }
        }

        private void DrawShape(Models.Shape shape)
        {
            var color = GetShapeColor(shape);
            var brush = new SolidColorBrush(color);
            var fillBrush = (ChkFillShapes?.IsChecked == true)
                ? new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B))
                : Brushes.Transparent;

            switch (shape.ShapeType.ToLower())
            {
                case "rectangle":
                    DrawRectangle(shape, brush, fillBrush);
                    break;
                case "polygon":
                case "rotation":
                    DrawPolygon(shape, brush, fillBrush);
                    break;
                case "circle":
                    DrawCircle(shape, brush, fillBrush);
                    break;
                case "line":
                    DrawLine(shape, brush);
                    break;
                case "point":
                    DrawPoint(shape, brush);
                    break;
            }

            // 绘制标签
            if (ChkShowLabels?.IsChecked == true && shape.Points.Count > 0)
            {
                DrawLabel(shape);
            }
        }

        private void DrawRectangle(Models.Shape shape, Brush stroke, Brush fill)
        {
            if (shape.Points.Count < 2) return;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Stroke = stroke,
                StrokeThickness = 2,
                Fill = fill,
                Tag = shape
            };

            var x1 = shape.Points[0][0];
            var y1 = shape.Points[0][1];
            var x2 = shape.Points[1][0];
            var y2 = shape.Points[1][1];

            Canvas.SetLeft(rect, Math.Min(x1, x2));
            Canvas.SetTop(rect, Math.Min(y1, y2));
            rect.Width = Math.Abs(x2 - x1);
            rect.Height = Math.Abs(y2 - y1);

            MainCanvas.Children.Add(rect);
        }

        private void DrawPolygon(Models.Shape shape, Brush stroke, Brush fill)
        {
            if (shape.Points.Count < 3) return;

            var polygon = new Polygon
            {
                Stroke = stroke,
                StrokeThickness = 2,
                Fill = fill,
                Tag = shape
            };

            var points = new PointCollection();
            foreach (var pt in shape.Points)
            {
                points.Add(new Point(pt[0], pt[1]));
            }
            polygon.Points = points;

            MainCanvas.Children.Add(polygon);
        }

        private void DrawCircle(Models.Shape shape, Brush stroke, Brush fill)
        {
            if (shape.Points.Count < 3) return;

            // 计算圆心和半径
            var centerX = shape.Points.Average(p => p[0]);
            var centerY = shape.Points.Average(p => p[1]);
            var radius = Math.Sqrt(Math.Pow(shape.Points[0][0] - centerX, 2) + 
                                  Math.Pow(shape.Points[0][1] - centerY, 2));

            var ellipse = new Ellipse
            {
                Stroke = stroke,
                StrokeThickness = 2,
                Fill = fill,
                Width = radius * 2,
                Height = radius * 2,
                Stretch = Stretch.Uniform,
                Tag = shape
            };

            Canvas.SetLeft(ellipse, centerX - radius);
            Canvas.SetTop(ellipse, centerY - radius);

            MainCanvas.Children.Add(ellipse);
        }

        private void DrawLine(Models.Shape shape, Brush stroke)
        {
            if (shape.Points.Count < 2) return;

            var line = new Line
            {
                X1 = shape.Points[0][0],
                Y1 = shape.Points[0][1],
                X2 = shape.Points[1][0],
                Y2 = shape.Points[1][1],
                Stroke = stroke,
                StrokeThickness = 2,
                Tag = shape
            };

            MainCanvas.Children.Add(line);
        }

        private void DrawPoint(Models.Shape shape, Brush stroke)
        {
            if (shape.Points.Count < 1) return;

            var ellipse = new Ellipse
            {
                Fill = stroke,
                Width = 8,
                Height = 8,
                Tag = shape
            };

            Canvas.SetLeft(ellipse, shape.Points[0][0] - 4);
            Canvas.SetTop(ellipse, shape.Points[0][1] - 4);

            MainCanvas.Children.Add(ellipse);
        }

        private void DrawLabel(Models.Shape shape)
        {
            if (shape.Points == null || shape.Points.Count == 0)
            {
                return;
            }

            var x = shape.Points[0][0];
            var y = shape.Points[0][1];

            var text = shape.Label;
            if (ChkShowConfidence?.IsChecked == true && shape.Score.HasValue)
            {
                text += $" {shape.Score.Value:P0}";
            }

            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 12,
                FontWeight = FontWeights.Bold
            };

            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y - 20);
            MainCanvas.Children.Add(textBlock);
        }

        private Color GetShapeColor(Models.Shape shape)
        {
            if (!string.IsNullOrEmpty(shape.Color))
            {
                try
                {
                    return (Color)ColorConverter.ConvertFromString(shape.Color);
                }
                catch { }
            }

            // 根据标签生成颜色
            var hash = shape.Label.GetHashCode();
            var r = (byte)((hash & 0xFF0000) >> 16);
            var g = (byte)((hash & 0x00FF00) >> 8);
            var b = (byte)(hash & 0x0000FF);
            return Color.FromRgb(r, g, b);
        }

        // ==================== 辅助方法 ====================
        
        private string? PromptForLabel()
        {
            // 获取可用类别列表
            var classes = new List<string> { "object", "person", "car", "dog", "cat" }; // 默认类别
            
            var dialog = new ClassSelectDialog(classes);
            if (dialog.ShowDialog() == true)
            {
                return dialog.SelectedClass;
            }
            return null;
        }

        private Models.Shape? ConvertBBoxToShape(BoundingBox bbox)
        {
            var points = new List<List<float>>();
            
            if (bbox.AnnotationType == "polygon" && bbox.Points?.Count > 0)
            {
                points = bbox.Points;
            }
            else
            {
                // 转换中心点格式为角点格式
                var x1 = bbox.X - bbox.Width / 2;
                var y1 = bbox.Y - bbox.Height / 2;
                var x2 = bbox.X + bbox.Width / 2;
                var y2 = bbox.Y + bbox.Height / 2;
                
                points.Add(new List<float> { x1, y1 });
                points.Add(new List<float> { x2, y2 });
            }

            return new Models.Shape
            {
                Label = bbox.ClassName,
                ShapeType = bbox.AnnotationType,
                Points = points,
                Score = bbox.Confidence
            };
        }

        // ==================== 按钮事件 ====================
        
        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentImageName) || _currentImage == null)
            {
                MessageBox.Show("请先加载图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var annotation = new AnnotationData
                {
                    ImageName = _currentImageName,
                    Width = _currentImage.PixelWidth,
                    Height = _currentImage.PixelHeight,
                    Bboxes = new List<BoundingBox>()
                };

                // 转换形状为边界框
                foreach (var shape in _shapes)
                {
                    var bbox = ConvertShapeToBBox(shape, _currentImage.PixelWidth, _currentImage.PixelHeight);
                    if (bbox != null)
                    {
                        annotation.Bboxes.Add(bbox);
                    }
                }

                var success = await _apiService.SaveAnnotationAsync(_currentImageName, annotation);
                if (success)
                {
                    MessageBox.Show("保存成功", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatus("标注已保存");
                }
                else
                {
                    MessageBox.Show("保存失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private BoundingBox? ConvertShapeToBBox(Models.Shape shape, int imageWidth, int imageHeight)
        {
            if (shape.Points.Count == 0) return null;

            var bbox = new BoundingBox
            {
                ClassName = shape.Label,
                AnnotationType = shape.ShapeType,
                Confidence = shape.Score ?? 0f
            };

            if (shape.ShapeType == "rectangle" && shape.Points.Count >= 2)
            {
                var x1 = shape.Points[0][0] / imageWidth;
                var y1 = shape.Points[0][1] / imageHeight;
                var x2 = shape.Points[1][0] / imageWidth;
                var y2 = shape.Points[1][1] / imageHeight;

                bbox.X = (x1 + x2) / 2;
                bbox.Y = (y1 + y2) / 2;
                bbox.Width = Math.Abs(x2 - x1);
                bbox.Height = Math.Abs(y2 - y1);
            }
            else if (shape.ShapeType == "polygon" || shape.ShapeType == "circle")
            {
                // 归一化多边形点
                bbox.Points = shape.Points.Select(p => new List<float>
                {
                    p[0] / imageWidth,
                    p[1] / imageHeight
                }).ToList();

                // 计算边界框
                var minX = shape.Points.Min(p => p[0]) / imageWidth;
                var minY = shape.Points.Min(p => p[1]) / imageHeight;
                var maxX = shape.Points.Max(p => p[0]) / imageWidth;
                var maxY = shape.Points.Max(p => p[1]) / imageHeight;

                bbox.X = (minX + maxX) / 2;
                bbox.Y = (minY + maxY) / 2;
                bbox.Width = maxX - minX;
                bbox.Height = maxY - minY;
            }

            return bbox;
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShape == null)
            {
                MessageBox.Show("请先选择一个形状", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // TODO: 实现形状编辑功能
            MessageBox.Show("形状编辑功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShape == null)
            {
                MessageBox.Show("请先选择一个形状", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"确定要删�?'{_selectedShape.Label}' 吗？", 
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _shapes.Remove(_selectedShape);
                _selectedShape = null;
                RedrawAllShapes();
                UpdateStatus("已删除形状");
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShape == null)
            {
                MessageBox.Show("请先选择一个形状", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 复制形状并偏移位�?
            var newShape = new Models.Shape
            {
                Label = _selectedShape.Label,
                ShapeType = _selectedShape.ShapeType,
                Points = _selectedShape.Points.Select(p => new List<float> { p[0] + 10, p[1] + 10 }).ToList(),
                Score = _selectedShape.Score,
                Color = _selectedShape.Color
            };

            _shapes.Add(newShape);
            RedrawAllShapes();
            UpdateStatus("已复制形状");
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要清空所有标注吗？", 
                "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                _shapes.Clear();
                _selectedShape = null;
                RedrawAllShapes();
                UpdateStatus("已清空所有标注");
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("导出功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShapesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedShape = ShapesList.SelectedItem as Models.Shape;
            if (_selectedShape != null)
            {
                UpdateStatus($"已选择: {_selectedShape.Label} ({_selectedShape.ShapeType})");
            }
        }

        private void Display_Changed(object sender, RoutedEventArgs e)
        {
            RedrawAllShapes();
        }

        private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ImageDisplay != null)
            {
                var scale = e.NewValue;
                ImageDisplay.LayoutTransform = new ScaleTransform(scale, scale);
                
                if (TxtZoom != null)
                {
                    TxtZoom.Text = $"{scale * 100:F0}%";
                }
            }
        }
    }
}

