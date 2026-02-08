using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using YoloAnnotator.Models;

namespace YoloAnnotator
{
    public partial class PolygonEditWindow : Window
    {
        private List<Point> _polygonPoints;
        private List<Point> _originalPoints;
        private readonly string _imagePath;
        
        // 编辑模式
        private enum EditMode { Select, AddVertex, DeleteVertex }
        private EditMode _currentMode = EditMode.Select;
        
        // 拖拽状态
        private bool _isDragging = false;
        private int _draggedVertexIndex = -1;
        private Point _dragStartPos;
        
        // 撤销/重做栈
        private Stack<List<Point>> _undoStack = new Stack<List<Point>>();
        private Stack<List<Point>> _redoStack = new Stack<List<Point>>();
        
        // 视觉元素
        private Polygon? _polygonShape;
        private List<Ellipse> _vertexMarkers = new List<Ellipse>();
        
        public List<Point> EditedPoints { get; private set; } = new List<Point>();
        
        public PolygonEditWindow(List<Point> points, string imagePath)
        {
            InitializeComponent();
            
            _polygonPoints = new List<Point>(points);
            _originalPoints = new List<Point>(points);
            _imagePath = imagePath;
            
            Loaded += async (s, e) => await InitializeAsync();
        }
        
        private async System.Threading.Tasks.Task InitializeAsync()
        {
            try
            {
                // 加载图片
                var bitmap = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(_imagePath, UriKind.Absolute));
                DisplayImage.Source = bitmap;
                
                // 设置 Canvas 大小
                ImageCanvas.Width = bitmap.PixelWidth;
                ImageCanvas.Height = bitmap.PixelHeight;
                PolygonCanvas.Width = bitmap.PixelWidth;
                PolygonCanvas.Height = bitmap.PixelHeight;
                
                // 绘制多边形
                DrawPolygon();
                UpdateStatistics();
                UpdateStatus("就绪 - 拖拽顶点调整位置");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }
        
        #region 绘制方法
        
        private void DrawPolygon()
        {
            PolygonCanvas.Children.Clear();
            _vertexMarkers.Clear();
            
            if (_polygonPoints.Count < 3) return;
            
            // 绘制多边形
            _polygonShape = new Polygon
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 255, 255))
            };
            
            foreach (var point in _polygonPoints)
            {
                _polygonShape.Points.Add(point);
            }
            
            PolygonCanvas.Children.Add(_polygonShape);
            
            // 绘制顶点
            for (int i = 0; i < _polygonPoints.Count; i++)
            {
                DrawVertex(_polygonPoints[i], i);
            }
        }
        
        private void DrawVertex(Point point, int index)
        {
            var marker = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = Brushes.White,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                Tag = index,
                Cursor = Cursors.Hand
            };
            
            Canvas.SetLeft(marker, point.X - 5);
            Canvas.SetTop(marker, point.Y - 5);
            
            marker.MouseLeftButtonDown += Vertex_MouseLeftButtonDown;
            marker.MouseMove += Vertex_MouseMove;
            marker.MouseLeftButtonUp += Vertex_MouseLeftButtonUp;
            
            PolygonCanvas.Children.Add(marker);
            _vertexMarkers.Add(marker);
        }
        
        #endregion
        
        #region 鼠标事件
        
        private void PolygonCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentMode == EditMode.AddVertex)
            {
                Point clickPoint = e.GetPosition(PolygonCanvas);
                AddVertexNearEdge(clickPoint);
            }
        }
        
        private void PolygonCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            // 由顶点的 MouseMove 处理
        }
        
        private void PolygonCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 由顶点的 MouseLeftButtonUp 处理
        }
        
        private void Vertex_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse marker && marker.Tag is int index)
            {
                if (_currentMode == EditMode.Select)
                {
                    // 开始拖拽
                    _isDragging = true;
                    _draggedVertexIndex = index;
                    _dragStartPos = e.GetPosition(PolygonCanvas);
                    marker.CaptureMouse();
                    e.Handled = true;
                }
                else if (_currentMode == EditMode.DeleteVertex)
                {
                    // 删除顶点
                    DeleteVertex(index);
                    e.Handled = true;
                }
            }
        }
        
        private void Vertex_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && _draggedVertexIndex >= 0 && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPos = e.GetPosition(PolygonCanvas);
                
                // 更新顶点位置
                _polygonPoints[_draggedVertexIndex] = currentPos;
                
                // 重新绘制
                DrawPolygon();
                UpdateStatistics();
            }
        }
        
        private void Vertex_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                
                if (sender is Ellipse marker)
                {
                    marker.ReleaseMouseCapture();
                }
                
                // 保存到撤销栈
                SaveState();
                _draggedVertexIndex = -1;
                
                UpdateStatus("顶点已移动");
            }
        }
        
        #endregion
        
        #region 工具按钮
        
        private void BtnSelectMode_Click(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.Select;
            BtnSelectMode.Style = (Style)FindResource("ActiveToolButtonStyle");
            BtnAddVertex.Style = (Style)FindResource("ToolButtonStyle");
            BtnDeleteVertex.Style = (Style)FindResource("ToolButtonStyle");
            UpdateStatus("选择模式 - 拖拽顶点调整位置");
        }
        
        private void BtnAddVertex_Click(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.AddVertex;
            BtnAddVertex.Style = (Style)FindResource("ActiveToolButtonStyle");
            BtnSelectMode.Style = (Style)FindResource("ToolButtonStyle");
            BtnDeleteVertex.Style = (Style)FindResource("ToolButtonStyle");
            UpdateStatus("添加顶点模式 - 点击边添加新顶点");
        }
        
        private void BtnDeleteVertex_Click(object sender, RoutedEventArgs e)
        {
            _currentMode = EditMode.DeleteVertex;
            BtnDeleteVertex.Style = (Style)FindResource("ActiveToolButtonStyle");
            BtnSelectMode.Style = (Style)FindResource("ToolButtonStyle");
            BtnAddVertex.Style = (Style)FindResource("ToolButtonStyle");
            UpdateStatus("删除顶点模式 - 点击顶点删除");
        }
        
        private void BtnSimplify_Click(object sender, RoutedEventArgs e)
        {
            SaveState();
            
            double tolerance = SliderSimplify.Value;
            _polygonPoints = SimplifyPolygon(_polygonPoints, tolerance);
            
            DrawPolygon();
            UpdateStatistics();
            UpdateStatus($"多边形已简化 - 顶点数：{_polygonPoints.Count}");
        }
        
        private void BtnSmooth_Click(object sender, RoutedEventArgs e)
        {
            SaveState();
            
            int iterations = (int)SliderSmooth.Value;
            _polygonPoints = SmoothPolygon(_polygonPoints, iterations);
            
            DrawPolygon();
            UpdateStatistics();
            UpdateStatus($"多边形已平滑 - 迭代次数：{iterations}");
        }
        
        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                _redoStack.Push(new List<Point>(_polygonPoints));
                _polygonPoints = _undoStack.Pop();
                DrawPolygon();
                UpdateStatistics();
                UpdateStatus("已撤销");
            }
        }
        
        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count > 0)
            {
                _undoStack.Push(new List<Point>(_polygonPoints));
                _polygonPoints = _redoStack.Pop();
                DrawPolygon();
                UpdateStatistics();
                UpdateStatus("已重做");
            }
        }
        
        #endregion
        
        #region 多边形操作
        
        private void AddVertexNearEdge(Point clickPoint)
        {
            if (_polygonPoints.Count < 2) return;
            
            // 找到最近的边
            int nearestEdgeIndex = -1;
            double minDistance = double.MaxValue;
            
            for (int i = 0; i < _polygonPoints.Count; i++)
            {
                int nextIndex = (i + 1) % _polygonPoints.Count;
                double distance = DistanceToSegment(clickPoint, 
                    _polygonPoints[i], _polygonPoints[nextIndex]);
                
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestEdgeIndex = i;
                }
            }
            
            // 如果距离足够近，添加顶点
            if (minDistance < 20 && nearestEdgeIndex >= 0)
            {
                SaveState();
                _polygonPoints.Insert(nearestEdgeIndex + 1, clickPoint);
                DrawPolygon();
                UpdateStatistics();
                UpdateStatus($"已添加顶点 - 总数：{_polygonPoints.Count}");
            }
        }
        
        private void DeleteVertex(int index)
        {
            if (_polygonPoints.Count <= 3)
            {
                MessageBox.Show("多边形至少需要3个顶点", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            SaveState();
            _polygonPoints.RemoveAt(index);
            DrawPolygon();
            UpdateStatistics();
            UpdateStatus($"已删除顶点 - 剩余：{_polygonPoints.Count}");
        }
        
        // Douglas-Peucker 算法简化多边形
        private List<Point> SimplifyPolygon(List<Point> points, double tolerance)
        {
            if (points.Count < 3) return points;
            
            return DouglasPeucker(points, tolerance);
        }
        
        private List<Point> DouglasPeucker(List<Point> points, double epsilon)
        {
            if (points.Count < 3) return points;
            
            // 找到距离首尾连线最远的点
            double maxDistance = 0;
            int maxIndex = 0;
            
            for (int i = 1; i < points.Count - 1; i++)
            {
                double distance = PerpendicularDistance(points[i], points[0], points[points.Count - 1]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    maxIndex = i;
                }
            }
            
            // 如果最大距离大于阈值，递归简化
            if (maxDistance > epsilon)
            {
                var left = DouglasPeucker(points.Take(maxIndex + 1).ToList(), epsilon);
                var right = DouglasPeucker(points.Skip(maxIndex).ToList(), epsilon);
                
                // 合并结果（移除重复点）
                var result = new List<Point>(left);
                result.AddRange(right.Skip(1));
                return result;
            }
            else
            {
                // 距离小于阈值，只保留首尾点
                return new List<Point> { points[0], points[points.Count - 1] };
            }
        }
        
        // 平滑多边形（Chaikin算法）
        private List<Point> SmoothPolygon(List<Point> points, int iterations)
        {
            if (points.Count < 3) return points;
            
            var smoothed = new List<Point>(points);
            
            for (int iter = 0; iter < iterations; iter++)
            {
                var newPoints = new List<Point>();
                
                for (int i = 0; i < smoothed.Count; i++)
                {
                    int nextIndex = (i + 1) % smoothed.Count;
                    
                    // 在每条边上插入两个点
                    Point p1 = new Point(
                        smoothed[i].X * 0.75 + smoothed[nextIndex].X * 0.25,
                        smoothed[i].Y * 0.75 + smoothed[nextIndex].Y * 0.25
                    );
                    
                    Point p2 = new Point(
                        smoothed[i].X * 0.25 + smoothed[nextIndex].X * 0.75,
                        smoothed[i].Y * 0.25 + smoothed[nextIndex].Y * 0.75
                    );
                    
                    newPoints.Add(p1);
                    newPoints.Add(p2);
                }
                
                smoothed = newPoints;
            }
            
            return smoothed;
        }
        
        #endregion
        
        #region 辅助方法
        
        private double DistanceToSegment(Point p, Point a, Point b)
        {
            double lengthSquared = Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2);
            
            if (lengthSquared == 0) return Distance(p, a);
            
            double t = Math.Max(0, Math.Min(1, 
                ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / lengthSquared));
            
            Point projection = new Point(
                a.X + t * (b.X - a.X),
                a.Y + t * (b.Y - a.Y)
            );
            
            return Distance(p, projection);
        }
        
        private double Distance(Point a, Point b)
        {
            return Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
        }
        
        private double PerpendicularDistance(Point point, Point lineStart, Point lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            
            double mag = Math.Sqrt(dx * dx + dy * dy);
            if (mag > 0)
            {
                dx /= mag;
                dy /= mag;
            }
            
            double pvx = point.X - lineStart.X;
            double pvy = point.Y - lineStart.Y;
            
            double pvdot = dx * pvx + dy * pvy;
            
            double dsx = pvdot * dx;
            double dsy = pvdot * dy;
            
            double ax = pvx - dsx;
            double ay = pvy - dsy;
            
            return Math.Sqrt(ax * ax + ay * ay);
        }
        
        private void SaveState()
        {
            _undoStack.Push(new List<Point>(_polygonPoints));
            _redoStack.Clear();
        }
        
        private void UpdateStatistics()
        {
            TxtVertexCount.Text = $"顶点数：{_polygonPoints.Count}";
            
            // 计算面积
            double area = CalculateArea(_polygonPoints);
            TxtArea.Text = $"面积：{area:F0} 像素²";
            
            // 计算周长
            double perimeter = CalculatePerimeter(_polygonPoints);
            TxtPerimeter.Text = $"周长：{perimeter:F0} 像素";
        }
        
        private double CalculateArea(List<Point> points)
        {
            if (points.Count < 3) return 0;
            
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                int j = (i + 1) % points.Count;
                area += points[i].X * points[j].Y;
                area -= points[j].X * points[i].Y;
            }
            
            return Math.Abs(area / 2);
        }
        
        private double CalculatePerimeter(List<Point> points)
        {
            if (points.Count < 2) return 0;
            
            double perimeter = 0;
            for (int i = 0; i < points.Count; i++)
            {
                int j = (i + 1) % points.Count;
                perimeter += Distance(points[i], points[j]);
            }
            
            return perimeter;
        }
        
        private void UpdateStatus(string message)
        {
            TxtStatus.Text = message;
        }
        
        #endregion
        
        #region 滑块事件
        
        private void SliderSimplify_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSimplifyValue != null)
            {
                TxtSimplifyValue.Text = e.NewValue.ToString("F1");
            }
        }
        
        private void SliderSmooth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtSmoothValue != null)
            {
                TxtSmoothValue.Text = ((int)e.NewValue).ToString();
            }
        }
        
        #endregion
        
        #region 按钮事件
        
        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            EditedPoints = _polygonPoints;
            DialogResult = true;
            Close();
        }
        
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        #endregion
    }
}
