using System.IO;
using System.Linq;
using System.Text.Json;

namespace VPet.Plugin.MeiChat.Memory
{
    /// <summary>
    /// 持久记忆管理器
    /// - 对话结束后自动提取
    /// - 重启/崩溃不丢失
    /// - 最多 30 条，自动淘汰最旧
    /// - 注入到 Agent 系统提示词
    /// </summary>
    public class MemoryManager
    {
        private readonly string _filePath;
        private List<MemoryFact> _facts = new();
        private const int MaxFacts = 30;

        /// <summary>最近一次提取时间，用于限频</summary>
        public DateTime LastExtractTime { get; set; } = DateTime.MinValue;

        public MemoryManager(string configDir)
        {
            _filePath = Path.Combine(configDir, "MeiChat.memory.json");
            Load();
        }

        /// <summary>加载记忆</summary>
        public void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    _facts = JsonSerializer.Deserialize<List<MemoryFact>>(json) ?? new();
                }
            }
            catch { _facts = new(); }

            // 清理软删除的
            _facts.RemoveAll(f => f.Deleted);
        }

        /// <summary>保存到磁盘</summary>
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _facts.RemoveAll(f => f.Deleted);
                var json = JsonSerializer.Serialize(_facts, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }

        /// <summary>添加一条记忆</summary>
        public void AddFact(string type, string content)
        {
            _facts.Add(new MemoryFact
            {
                Type = type,
                Content = content,
                Timestamp = DateTime.Now
            });

            // 超过上限淘汰最旧
            while (_facts.Count > MaxFacts)
                _facts.RemoveAt(0);

            Save();
        }

        /// <summary>批量添加（AI 提取后调用）</summary>
        public void AddFacts(IEnumerable<MemoryFact> facts)
        {
            foreach (var f in facts)
            {
                if (!string.IsNullOrWhiteSpace(f.Content))
                {
                    f.Timestamp = DateTime.Now;
                    _facts.Add(f);
                }
            }

            while (_facts.Count > MaxFacts)
                _facts.RemoveAt(0);

            Save();
        }

        /// <summary>删除记忆（AI 可调用）</summary>
        public void Forget(string keyword)
        {
            _facts.RemoveAll(f =>
                f.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        /// <summary>获取记忆上下文文本（注入到系统提示词）</summary>
        public string GetMemoryContext()
        {
            if (_facts.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n\n===== 我的记忆 =====");
            sb.AppendLine("以下是之前记住的信息（按时间排列）：");

            for (int i = 0; i < _facts.Count; i++)
            {
                sb.AppendLine(_facts[i].ToContextLine(i + 1));
            }

            sb.AppendLine("你可以自然地在对话中使用这些记忆。");
            sb.AppendLine("如果用户提到新的重要信息，你可以用 remember 工具记住。");
            return sb.ToString();
        }

        /// <summary>获取记忆统计</summary>
        public int Count => _facts.Count;

        /// <summary>获取所有记忆（副本）</summary>
        public List<MemoryFact> GetAllFacts() => new List<MemoryFact>(_facts);

        /// <summary>搜索记忆</summary>
        public List<MemoryFact> Search(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return GetAllFacts();
            return _facts.Where(f =>
                f.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                f.Type.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>按索引删除记忆</summary>
        public bool DeleteAt(int index)
        {
            if (index < 0 || index >= _facts.Count) return false;
            _facts.RemoveAt(index);
            Save();
            return true;
        }

        /// <summary>批量删除</summary>
        public int DeleteRange(IEnumerable<int> indices)
        {
            var sorted = indices.Where(i => i >= 0 && i < _facts.Count)
                                .Distinct().OrderByDescending(i => i).ToList();
            foreach (var i in sorted)
                _facts.RemoveAt(i);
            Save();
            return sorted.Count;
        }

        /// <summary>清空所有记忆</summary>
        public void ClearAll()
        {
            _facts.Clear();
            Save();
        }

        /// <summary>导出记忆到 JSON 文件</summary>
        public string Export(string filePath)
        {
            var json = JsonSerializer.Serialize(_facts, new JsonSerializerOptions { WriteIndented = true });
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, json);
            return filePath;
        }

        /// <summary>获取记忆类型统计</summary>
        public Dictionary<string, int> GetTypeStats()
        {
            return _facts.GroupBy(f => f.Type)
                         .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
