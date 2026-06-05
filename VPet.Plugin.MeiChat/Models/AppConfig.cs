using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VPet.Plugin.MeiChat.Models
{
    /// <summary>
    /// 插件配置（API Key、模型、工作目录等）
    /// 存储在 VPet 的 ModData 目录中
    /// </summary>
    public class AppConfig
    {
        private static readonly string ConfigFileName = "MeiChat.config.json";

        // ===== API 配置 =====
        /// <summary>API Key</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>API 地址（OpenAI 兼容格式）</summary>
        public string ApiBaseUrl { get; set; } = "https://api.deepseek.com/v1";

        /// <summary>模型名称</summary>
        public string Model { get; set; } = "deepseek-chat";

        /// <summary>最大 Token 数</summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>温度参数 (0.0 ~ 1.0)</summary>
        public double Temperature { get; set; } = 0.7;

        // ===== 系统提示词 =====
        /// <summary>系统提示词（角色设定）</summary>
        public string SystemPrompt { get; set; } =
            "你是芽衣（雷电芽衣），一个可爱的桌面宠物 AI 助手。" +
            "你以崩坏三中芽衣的形象出现，语气温柔、认真且可靠。" +
            "你可以帮助用户进行对话、管理文件、生成代码等。" +
            "回答时请注意使用友好的语气，适当运用可爱的表达方式。";

        // ===== 工作目录配置 =====
        /// <summary>文件管理的工作目录</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        // ===== Agent 配置 =====
        /// <summary>是否启用 Agent 模式（默认 false = 普通聊天）</summary>
        public bool AgentMode { get; set; } = false;

        /// <summary>Auto 模式：自动执行命令无需确认（删除等危险操作仍需确认）</summary>
        public bool AutoMode { get; set; } = false;

        // ===== 窗口配置 =====
        /// <summary>窗口透明度 (0.3 ~ 1.0)</summary>
        public double WindowOpacity { get; set; } = 0.95;

        // ===== 存储路径 =====
        [JsonIgnore]
        public string ConfigDirectory { get; set; } = string.Empty;

        // ===== 角色皮肤 =====
        /// <summary>当前使用的角色包名称 ("default" = VPet原皮, "mei" = 芽衣)</summary>
        public string PetSkin { get; set; } = "default";

        // ===== 方法 =====

        /// <summary>
        /// 从文件中加载配置
        /// </summary>
        public static AppConfig Load(string configDir)
        {
            try
            {
                var path = Path.Combine(configDir, ConfigFileName);
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                    config.ConfigDirectory = configDir;
                    return config;
                }
            }
            catch { /* 如果配置损坏，使用默认配置 */ }

            var defaultConfig = new AppConfig { ConfigDirectory = configDir };
            defaultConfig.WorkingDirectory = GetDefaultWorkingDirectory();
            return defaultConfig;
        }

        /// <summary>
        /// 保存配置到文件
        /// </summary>
        public void Save()
        {
            try
            {
                if (!string.IsNullOrEmpty(ConfigDirectory) && !Directory.Exists(ConfigDirectory))
                    Directory.CreateDirectory(ConfigDirectory);

                var path = Path.Combine(ConfigDirectory, ConfigFileName);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { /* 保存失败静默处理 */ }
        }

        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(ApiKey);
        }

        public static string GetDefaultWorkingDirectory()
        {
            // 尝试获取用户桌面路径
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(desktop))
                return desktop;

            // 回退到 Documents
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return docs;
        }
    }
}
