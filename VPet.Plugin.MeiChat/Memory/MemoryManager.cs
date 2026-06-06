using System.IO;
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
    }
}
