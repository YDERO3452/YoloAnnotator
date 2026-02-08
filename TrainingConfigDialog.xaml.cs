using System.Windows;

namespace YoloAnnotator
{
    /// <summary>
    /// 训练参数配置对话框
    /// </summary>
    public partial class TrainingConfigDialog : Window
    {
        public int Epochs { get; private set; } = 100;
        public int BatchSize { get; private set; } = 16;
        public int ImageSize { get; private set; } = 640;
        public string Device { get; private set; } = "0";
        public string ModelType { get; private set; } = "yolov8n";
        public string WeightsPath { get; private set; } = "";

        public TrainingConfigDialog(bool hasCuda, string? cudaDeviceName = null, int cudaDeviceCount = 0, double? cudaMemoryGb = null, string? cudaReason = null, string? pytorchVersion = null, string? cudaVersion = null, string? driverVersion = null, string? computeCapability = null, string? system = null, string? pythonVersion = null)
        {
            InitializeComponent();
            
            // 设置窗口所有者
            Owner = Application.Current.MainWindow;

            // 初始化设备选择
            CboDevice.Items.Add("CPU");
            if (hasCuda)
            {
                CboDevice.Items.Add("GPU (CUDA)");
                CboDevice.SelectedIndex = 1;
            }
            else
            {
                CboDevice.SelectedIndex = 0;
            }

            // 初始化模型类型 - 添加 YOLOv8 和 YOLOv26 两个系列
            CboModelType.Items.Add("YOLOv8n (Nano - 最快)");
            CboModelType.Items.Add("YOLOv8s (Small)");
            CboModelType.Items.Add("YOLOv8m (Medium)");
            CboModelType.Items.Add("YOLOv8l (Large)");
            CboModelType.Items.Add("YOLOv8x (XLarge)");
            CboModelType.Items.Add("--- 分隔 ---");
            CboModelType.Items.Add("YOLOv26n (Nano - 自定义)");
            CboModelType.Items.Add("YOLOv26s (Small - 自定义)");
            CboModelType.Items.Add("YOLOv26m (Medium - 自定义)");
            CboModelType.SelectedIndex = 0;

            // 设置 CUDA 状态信息
            if (hasCuda)
            {
                var cudaInfo = "✓ CUDA 可用";
                if (!string.IsNullOrEmpty(cudaDeviceName))
                {
                    cudaInfo += $"\n  设备: {cudaDeviceName}";
                }
                if (cudaDeviceCount > 0)
                {
                    cudaInfo += $"\n  GPU 数量: {cudaDeviceCount}";
                }
                if (cudaMemoryGb.HasValue)
                {
                    cudaInfo += $"\n  显存: {cudaMemoryGb:F1} GB";
                }
                if (!string.IsNullOrEmpty(cudaVersion))
                {
                    cudaInfo += $"\n  CUDA 版本: {cudaVersion}";
                }
                if (!string.IsNullOrEmpty(driverVersion))
                {
                    cudaInfo += $"\n  驱动版本: {driverVersion}";
                }
                if (!string.IsNullOrEmpty(computeCapability))
                {
                    cudaInfo += $"\n  计算能力: {computeCapability}";
                }
                if (!string.IsNullOrEmpty(pytorchVersion))
                {
                    cudaInfo += $"\n  PyTorch 版本: {pytorchVersion}";
                }
                cudaInfo += "\n  将使用 GPU 加速训练";
                
                TxtCudaStatus.Text = cudaInfo;
                TxtCudaStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
                BorderCudaStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
            }
            else
            {
                var cudaInfo = "⚠ CUDA 不可用";
                if (!string.IsNullOrEmpty(cudaReason))
                {
                    cudaInfo += $"\n  原因: {cudaReason}";
                }
                if (!string.IsNullOrEmpty(pytorchVersion))
                {
                    cudaInfo += $"\n  PyTorch 版本: {pytorchVersion}";
                }
                if (!string.IsNullOrEmpty(cudaVersion))
                {
                    cudaInfo += $"\n  CUDA 版本: {cudaVersion}";
                }
                if (!string.IsNullOrEmpty(system))
                {
                    cudaInfo += $"\n  系统: {system}";
                }
                if (!string.IsNullOrEmpty(pythonVersion))
                {
                    cudaInfo += $"\n  Python 版本: {pythonVersion}";
                }
                cudaInfo += "\n  将使用 CPU 训练（速度较慢）";
                
                TxtCudaStatus.Text = cudaInfo;
                TxtCudaStatus.Foreground = System.Windows.Media.Brushes.Orange;
                BorderCudaStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(249, 115, 22));
            }
        }

        private void BtnBrowseWeights_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "权重文件|*.pt;*.pth;*.onnx|所有文件|*.*",
                Title = "选择预训练权重文件"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtWeightsPath.Text = dialog.FileName;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtEpochs.Text, out int epochs) || epochs <= 0)
            {
                MessageBox.Show("请输入有效的训练轮数", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtBatchSize.Text, out int batchSize) || batchSize <= 0)
            {
                MessageBox.Show("请输入有效的批次大小", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtImageSize.Text, out int imageSize) || imageSize <= 0)
            {
                MessageBox.Show("请输入有效的图片大小", "参数错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Epochs = epochs;
            BatchSize = batchSize;
            ImageSize = imageSize;
            
            // 设备选择：CPU 或 GPU
            if (CboDevice.SelectedIndex == 0)
            {
                Device = "cpu";
            }
            else
            {
                Device = "0";
            }

            // 模型类型 - 修复bug：使用 CboModelType.SelectedIndex 而不是 CboDevice.SelectedIndex
            ModelType = CboModelType.SelectedIndex switch
            {
                0 => "yolov8n",  // YOLOv8n
                1 => "yolov8s",  // YOLOv8s
                2 => "yolov8m",  // YOLOv8m
                3 => "yolov8l",  // YOLOv8l
                4 => "yolov8x",  // YOLOv8x
                5 => "---",     // 分隔符，跳过
                6 => "yolo26n",  // YOLOv26n
                7 => "yolo26s",  // YOLOv26s
                8 => "yolo26m",  // YOLOv26m
                _ => "yolov8n"  // 默认
            };

            // 如果选择了分隔符，默认使用 YOLOv8n
            if (ModelType == "---")
            {
                ModelType = "yolov8n";
            }

            WeightsPath = TxtWeightsPath.Text;

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