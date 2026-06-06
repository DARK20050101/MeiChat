using System.Text.Json;

namespace VPet.Plugin.MeiChat.Agent.Tools
{
    /// <summary>
    /// 桌宠控制工具 — 让AI操控桌宠执行原生动作
    /// </summary>
    public class PetControlTool : ITool
    {
        private readonly Main _plugin;

        public PetControlTool(Main plugin) => _plugin = plugin;

        public string Name => "control_pet";
        public string Description => "控制桌宠执行原生动作：工作、睡觉、查看状态、改变心情、互动等。";

        public object Parameters => new
        {
            type = "object",
            properties = new
            {
                action = new
                {
                    type = "string",
                    description = "要执行的动作: work（开始工作）| sleep（睡觉）| wakeup（起床）| check（查看状态）| mood（改变心情: happy/normal/sad）| say（说话）| list_work（列出可用工作）| animate（播放动画: touch_head/touch_body/default）"
                },
                param = new
                {
                    type = "string",
                    description = "动作参数（可选）：work名、说话内容等"
                }
            },
            required = new[] { "action" }
        };

        public async Task<ToolResult> ExecuteAsync(string argumentsJson, string workingDir, CancellationToken ct)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
                if (args == null || !args.TryGetValue("action", out var action))
                    return ToolResult.Fail("缺少参数: action");

                var param = args.TryGetValue("param", out var p) ? p : "";

                return await _plugin.MW.Dispatcher.InvokeAsync(() =>
                {
                    var main = _plugin.MW.Main;
                    var save = main.Core.Save;

                    switch (action)
                    {
                        case "list_work":
                            return ListWorks(main);

                        case "work":
                            return StartWork(main, param);

                        case "sleep":
                            main.DisplaySleep(true);
                            return ToolResult.Ok("😴 好的，我去睡一会儿");

                        case "wakeup":
                            main.DisplaySleep(false);
                            main.DisplayDefault();
                            return ToolResult.Ok("🌅 早上好！我起来了");

                        case "check":
                            return CheckStatus(save);

                        case "mood":
                            return SetMood(save, param);

                        case "say":
                            var text = string.IsNullOrEmpty(param) ? "嗨~" : param;
                            main.Say(text);
                            return ToolResult.Ok($"💬 说了: {text}");

                        case "animate":
                            return PlayAnimate(main, param);

                        default:
                            return ToolResult.Fail($"未知动作: {action}");
                    }
                }).Task;
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"控制桌宠失败: {ex.Message}");
            }
        }

        private ToolResult ListWorks(VPet_Simulator.Core.Main main)
        {
            main.WorkList(out var works, out var studies, out var plays);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("📋 可用工作：");
            if (works != null)
                foreach (var w in works) sb.AppendLine($"  💼 {w.NameTrans ?? w.Name}（等级{w.LevelLimit}，{w.Time}分钟，基础{w.MoneyBase}）");

            sb.AppendLine("\n📚 学习：");
            if (studies != null)
                foreach (var s in studies) sb.AppendLine($"  📖 {s.NameTrans ?? s.Name}（等级{s.LevelLimit}，{s.Time}分钟）");

            sb.AppendLine("\n🎮 娱乐：");
            if (plays != null)
                foreach (var p in plays) sb.AppendLine($"  🎯 {p.NameTrans ?? p.Name}（{p.Time}分钟）");

            return ToolResult.Ok(sb.ToString().TrimEnd());
        }

        private ToolResult StartWork(VPet_Simulator.Core.Main main, string workName)
        {
            main.WorkList(out var works, out var studies, out var plays);
            var allWorks = new List<VPet_Simulator.Core.GraphHelper.Work>();
            if (works != null) allWorks.AddRange(works);
            if (studies != null) allWorks.AddRange(studies);
            if (plays != null) allWorks.AddRange(plays);

            VPet_Simulator.Core.GraphHelper.Work? target = null;

            if (!string.IsNullOrWhiteSpace(workName))
            {
                target = allWorks.FirstOrDefault(w =>
                    (w.NameTrans ?? w.Name).Contains(workName, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                    return ToolResult.Fail($"找不到工作「{workName}」，用 list_work 查看可用工作");
            }
            else
            {
                target = allWorks.FirstOrDefault();
            }

            if (target == null)
                return ToolResult.Fail("没有可用工作");

            main.StartWork(target);
            return ToolResult.Ok($"✅ 开始工作: {target.NameTrans ?? target.Name}");
        }

        private ToolResult CheckStatus(VPet_Simulator.Core.IGameSave save)
        {
            return ToolResult.Ok(
                $"📊 当前状态\n" +
                $"名字: {save.Name}\n" +
                $"等级: {save.Level}  经验: {save.Exp}\n" +
                $"金钱: {save.Money}\n" +
                $"体力: {save.Strength}/{save.StrengthMax}\n" +
                $"饱腹: {save.StrengthFood:F0}/100\n" +
                $"口渴: {save.StrengthDrink:F0}/100\n" +
                $"心情: {save.Feeling}/{save.FeelingMax}\n" +
                $"好感: {save.Likability}/{save.LikabilityMax}\n" +
                $"模式: {save.Mode}"
            );
        }

        private ToolResult SetMood(VPet_Simulator.Core.IGameSave save, string mood)
        {
            switch (mood.ToLower())
            {
                case "happy":
                    save.FeelingChange(50);
                    return ToolResult.Ok("😊 心情变好了！");
                case "normal":
                    save.Mode = VPet_Simulator.Core.IGameSave.ModeType.Nomal;
                    return ToolResult.Ok("😐 回到平常状态");
                case "sad":
                    save.FeelingChange(-30);
                    return ToolResult.Ok("😢 有点难过...");
                default:
                    return ToolResult.Fail("可用心情: happy / normal / sad");
            }
        }

        private ToolResult PlayAnimate(VPet_Simulator.Core.Main main, string anim)
        {
            switch (anim.ToLower())
            {
                case "touch_head":
                    main.DisplayTouchHead?.Invoke();
                    return ToolResult.Ok("☺️ 摸了摸头");
                case "touch_body":
                    main.DisplayTouchBody?.Invoke();
                    return ToolResult.Ok("☺️ 摸了摸");
                case "default":
                    main.DisplayDefault();
                    return ToolResult.Ok("👋 回到默认状态");
                case "move":
                    main.DisplayMove?.Invoke();
                    return ToolResult.Ok("🚶 走一走");
                case "idel":
                    main.DisplayIdel?.Invoke();
                    return ToolResult.Ok("💤 发会儿呆");
                default:
                    main.Say(anim);
                    return ToolResult.Ok($"💬 播放动画: {anim}");
            }
        }
    }
}
