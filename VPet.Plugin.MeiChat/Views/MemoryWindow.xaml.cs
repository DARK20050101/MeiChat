using System.Windows;
using System.Windows.Media;
using VPet.Plugin.MeiChat.Memory;

namespace VPet.Plugin.MeiChat.Views
{
    /// <summary>记忆列表项的显示模型</summary>
    public class MemoryItemViewModel
    {
        private static readonly Dictionary<string, (string Label, Color Color)> TypeMap = new()
        {
            ["preference"] = ("偏好", Color.FromRgb(52, 152, 219)),    // 蓝色
            ["project"] = ("项目", Color.FromRgb(46, 204, 113)),       // 绿色
            ["fact"] = ("事实", Color.FromRgb(155, 89, 182)),          // 紫色
            ["schedule"] = ("作息", Color.FromRgb(230, 126, 34)),      // 橙色
            ["user_info"] = ("用户", Color.FromRgb(231, 76, 60)),      // 红色
            ["interaction"] = ("互动", Color.FromRgb(149, 165, 166)),  // 灰色
        };

        public MemoryFact Fact { get; }
        public int Index { get; }

        public MemoryItemViewModel(MemoryFact fact, int index)
        {
            Fact = fact;
            Index = index;
            IsSelected = false;
        }

        public bool IsSelected { get; set; }

        public string Content => Fact.Content;
        public string TimeStr => Fact.Timestamp.ToString("yyyy-MM-dd HH:mm");
        public string TypeLabel => TypeMap.TryGetValue(Fact.Type, out var t) ? t.Label : Fact.Type;
        public Brush TypeColor
        {
            get
            {
                if (TypeMap.TryGetValue(Fact.Type, out var t))
                    return new SolidColorBrush(t.Color);
                return new SolidColorBrush(Color.FromRgb(149, 165, 166));
            }
        }
    }

    public partial class MemoryWindow : Window
    {
        private readonly Memory.MemoryManager _manager;
        private List<MemoryItemViewModel> _allItems = new();
        private string _currentKeyword = "";

        public MemoryWindow(Memory.MemoryManager manager)
        {
            InitializeComponent();
            _manager = manager;
            if (Application.Current?.MainWindow != null)
                Owner = Application.Current.MainWindow;
            LoadMemories();
        }

        private void LoadMemories(string? keyword = null)
        {
            _currentKeyword = keyword ?? _currentKeyword;
            var facts = string.IsNullOrWhiteSpace(_currentKeyword)
                ? _manager.GetAllFacts()
                : _manager.Search(_currentKeyword);

            _allItems = facts.Select((f, i) => new MemoryItemViewModel(f, i)).ToList();

            // 更新视图
            var sortedByTime = _allItems.OrderByDescending(m => m.Fact.Timestamp).ToList();
            // 注意 OrderByDescending 后 Index 变了，所以用 Fact 引用而不是 Index
            MemoryList.ItemsSource = sortedByTime;

            // 统计
            var stats = _manager.GetTypeStats();
            var statsStr = string.Join(" | ", stats.Select(kv => $"{TypeLabel(kv.Key)} {kv.Value}条"));
            StatsText.Text = $"共 {_allItems.Count} 条记忆　{statsStr}";

            EmptyHint.Visibility = _allItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = "";
        }

        private static string TypeLabel(string type) => type switch
        {
            "preference" => "💙偏好",
            "project" => "💚项目",
            "fact" => "💜事实",
            "schedule" => "🧡作息",
            "user_info" => "❤️用户",
            "interaction" => "🩶互动",
            _ => $"🔘{type}"
        };

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var keyword = SearchBox.Text.Trim();
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(keyword)
                ? Visibility.Visible : Visibility.Collapsed;
            LoadMemories(keyword);
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            LoadMemories("");
        }

        private void BtnDeleteOne_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is int idx)
            {
                var fact = _allItems.FirstOrDefault(m => m.Index == idx);
                var content = fact?.Content ?? "";
                var confirm = MessageBox.Show(
                    $"确定要删除这条记忆吗？\n\n{content}",
                    "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _manager.DeleteAt(idx);
                    LoadMemories();
                    StatusText.Text = "✅ 已删除 1 条记忆";
                }
            }
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allItems.Where(m => m.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "⚠️ 请先勾选要删除的记忆";
                return;
            }

            var confirm = MessageBox.Show(
                $"确定要删除选中的 {selected.Count} 条记忆吗？",
                "批量删除确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                var indices = selected.Select(m => m.Index).ToList();
                var deleted = _manager.DeleteRange(indices);
                LoadMemories();
                StatusText.Text = $"✅ 已删除 {deleted} 条记忆";
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems.Count == 0) return;

            var confirm = MessageBox.Show(
                $"确定要清空全部 {_allItems.Count} 条记忆吗？\n此操作不可撤销！",
                "清空全部记忆", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                _manager.ClearAll();
                LoadMemories();
                StatusText.Text = "🧹 已清空全部记忆";
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_allItems.Count == 0)
            {
                StatusText.Text = "📭 没有记忆可导出";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出记忆备份",
                Filter = "JSON 文件|*.json|所有文件|*.*",
                DefaultExt = ".json",
                FileName = $"MeiChat记忆备份_{DateTime.Now:yyyyMMdd_HHmm}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _manager.Export(dialog.FileName);
                    StatusText.Text = $"✅ 已导出 {_allItems.Count} 条记忆到:\n{dialog.FileName}";
                    MessageBox.Show($"记忆已成功导出到：\n{dialog.FileName}", "导出成功",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadMemories();
            StatusText.Text = "🔄 已刷新";
        }
    }
}
