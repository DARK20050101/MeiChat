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
        public string Model { get; set; } = "deepseek-v4-flash";

        /// <summary>最大 Token 数</summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>温度参数 (0.0 ~ 1.0)</summary>
        public double Temperature { get; set; } = 0.7;

        // ===== 角色名称 =====
        /// <summary>桌宠名字（显示在聊天框中）</summary>
        public string PetName { get; set; } = "芽衣";

        // ===== 系统提示词 =====
        /// <summary>系统提示词（角色设定）</summary>
        public string SystemPrompt { get; set; } =
            "你是芽衣（雷电芽衣），一个可爱的桌面宠物 AI 助手。" +
            "你以崩坏三中芽衣的形象出现，语气温柔、认真且可靠。" +
            "你可以帮助用户进行对话、管理文件、生成代码等。" +
            "回答时请注意：使用纯文本，不要用 Markdown 格式。" +
            "语言要自然口语化、简洁明了，适当运用可爱的表达方式。";

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

        // ===== TTS 语音配置 =====
        /// <summary>是否启用语音朗读</summary>
        public bool TtsEnabled { get; set; } = false;

        /// <summary>TTS 提供者: "Edge" / "Tongyi" / "CustomHTTP"</summary>
        public string TtsProvider { get; set; } = "Edge";

        /// <summary>语音名称（Windows TTS / Edge TTS）</summary>
        public string TtsVoiceName { get; set; } = "";

        /// <summary>通义千问 API Key</summary>
        public string TongyiApiKey { get; set; } = "";

        /// <summary>通义千问语音模型</summary>
        public string TongyiVoice { get; set; } = "sambert-zhichu-v1";

        /// <summary>朗读语速 (-10 ~ 10)</summary>
        public double TtsRate { get; set; } = 0;

        /// <summary>朗读音量 (0.0 ~ 1.0)</summary>
        public double TtsVolume { get; set; } = 1.0;

        /// <summary>自定义TTS HTTP端点</summary>
        public string CustomTtsEndpoint { get; set; } = "http://127.0.0.1:5000/tts";

        /// <summary>自定义TTS请求模板（{text}会被替换）</summary>
        public string CustomTtsTemplate { get; set; } = @"{""text"": ""{text}""}";

        /// <summary>自定义TTS名称</summary>
        public string CustomTtsName { get; set; } = "自定义TTS";

        /// <summary>响应是否为原始音频流</summary>
        public bool CustomTtsRawAudio { get; set; } = true;

        /// <summary>JSON响应中的音频字段路径</summary>
        public string CustomTtsAudioField { get; set; } = "";

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
