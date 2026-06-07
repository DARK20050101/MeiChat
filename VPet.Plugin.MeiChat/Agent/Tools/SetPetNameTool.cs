using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 修改桌宠名字 — AI 可通过此工具修改显示名称并保存
    /// </summary>
    public class SetPetNameTool : ITool
    {
        private readonly Main _plugin;

        public SetPetNameTool(Main plugin) => _plugin = plugin;

        public string Name => "set_pet_name";
        public string Description => "修改桌宠的显示名字。用户说「以后叫我xxx」或「给你改名为xxx」时调用。改名后会保存到设置中，重启也有效。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                name = new
                {
                    type = "string",
                    description = "新的名字"
                }
            },
            required = new[] { "name" }
        };

        public Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
                    return Task.FromResult(ToolResult.Fail("缺少参数: name"));

                name = name.Trim();
                if (name.Length > 20) name = name[..20];

                var oldName = _plugin.Config.PetName;
                _plugin.Config.PetName = name;
                _plugin.Config.Save();

                // 也记到记忆里
                _plugin.Memory?.AddFact("user_info", $"桌宠名字改为 {name}（原为 {oldName}）");

                // 通知 TalkBox 更新系统提示词中的名字
                _plugin.ReinitializeAgentEngine();

                return Task.FromResult(ToolResult.Ok($"✅ 名字已从「{oldName}」改为「{name}」"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Fail($"改名失败: {ex.Message}"));
            }
        }
    }
}
