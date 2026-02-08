using System.Collections.Generic;
using System.Windows;

namespace YoloAnnotator
{
    public partial class ClassSelectDialog : Window
    {
        private List<string> _classes;

        // 类别颜色列表，与MainWindow保持一致
        private readonly string[] _classColors = new[]
        {
            "#ef4444", // 红色
            "#22c55e", // 绿色
            "#3b82f6", // 蓝色
            "#f59e0b", // 橙色
            "#8b5cf6", // 紫色
            "#ec4899", // 粉色
            "#06b6d4", // 青色
            "#84cc16", // 酸橙色
            "#f97316", // 深橙色
            "#14b8a6", // 蓝绿色
            "#a855f7", // 浅紫色
            "#eab308", // 黄色
        };

        public string SelectedClass { get; private set; } = string.Empty;

        public ClassSelectDialog(List<string> classes)
        {
            InitializeComponent();
            _classes = classes ?? new List<string>();
            LoadClasses();
            TxtClassName.Focus();
        }

        private void LoadClasses()
        {
            var classItems = new List<ClassItem>();
            for (int i = 0; i < _classes.Count; i++)
            {
                classItems.Add(new ClassItem
                {
                    Name = _classes[i],
                    Id = i,
                    Color = _classColors[i % _classColors.Length]
                });
            }
            
            LstClasses.ItemsSource = null;
            LstClasses.ItemsSource = classItems;
            TxtClassCount.Text = $"共 {_classes.Count} 个类别";
        }

        private void BtnAddClass_Click(object sender, RoutedEventArgs e)
        {
            var newClass = TxtClassName.Text.Trim();
            if (!string.IsNullOrEmpty(newClass))
            {
                if (!_classes.Contains(newClass))
                {
                    _classes.Add(newClass);
                    LoadClasses();
                    
                    // 选中新添加的类别
                    if (LstClasses.Items.Count > 0)
                    {
                        LstClasses.SelectedIndex = _classes.Count - 1;
                        LstClasses.ScrollIntoView(LstClasses.SelectedItem);
                    }
                    
                    TxtClassName.Clear();
                }
                else
                {
                    MessageBox.Show("该类别已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void LstClasses_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (LstClasses.SelectedItem is ClassItem selectedClass)
            {
                SelectedClass = selectedClass.Name;
                System.Diagnostics.Debug.WriteLine($"[ClassSelectDialog] DoubleClick: SelectedClass='{SelectedClass}'");
                DialogResult = true;
                Close();
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            string selectedClass = string.Empty;

            if (LstClasses.SelectedItem is ClassItem selectedClassItem)
            {
                selectedClass = selectedClassItem.Name;
            }
            else if (!string.IsNullOrEmpty(TxtClassName.Text.Trim()))
            {
                selectedClass = TxtClassName.Text.Trim();
            }
            
            if (!string.IsNullOrEmpty(selectedClass))
            {
                SelectedClass = selectedClass;
                System.Diagnostics.Debug.WriteLine($"[ClassSelectDialog] BtnOK: SelectedClass='{SelectedClass}'");
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("请选择或输入类别", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                // 不设置 DialogResult，保持对话框打开
            }
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

        private void TxtClassName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                // 如果文本框有内容且是已有类别，直接选择
                var input = TxtClassName.Text.Trim();
                System.Diagnostics.Debug.WriteLine($"[ClassSelectDialog] Enter key: input='{input}'");
                
                if (!string.IsNullOrEmpty(input) && _classes.Contains(input))
                {
                    SelectedClass = input;
                    System.Diagnostics.Debug.WriteLine($"[ClassSelectDialog] Enter key: SelectedClass='{SelectedClass}' (existing)");
                    DialogResult = true;
                    Close();
                }
                else
                {
                    BtnOK_Click(null!, null!);
                }
            }
        }

        // 类别显示项
        private class ClassItem
        {
            public string Name { get; set; } = string.Empty;
            public int Id { get; set; }
            public string Color { get; set; } = string.Empty;
        }
    }
}