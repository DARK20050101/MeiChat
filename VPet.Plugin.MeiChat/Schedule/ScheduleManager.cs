using VPet.Plugin.MeiChat.Memory;

namespace VPet.Plugin.MeiChat.Schedule
{
    /// <summary>
    /// 作息调度引擎
    /// - 每分钟检查一次
    /// - 到点触发：说话 + 记录记忆 + 动作
    /// - 支持用户通过对话修改
    /// </summary>
    public class ScheduleManager : IDisposable
    {
        private readonly Main _plugin;
        private readonly MemoryManager _memory;
        private Timer? _timer;
        private List<ScheduleSlot> _slots;
        private readonly HashSet<string> _todayTriggered = new(); // 防止一天内重复触发

        /// <summary>默认作息</summary>
        public static List<ScheduleSlot> DefaultSchedule() => new()
        {
            new() { Name = "起床",      Hour = 8,  Minute = 0,  Action = "wakeup", Icon = "", Speech = "早安！新的一天开始了~今天也要加油哦！" },
            new() { Name = "上午工作",  Hour = 9,  Minute = 0,  Action = "work",   Icon = "", Speech = "开始工作啦！有什么需要我帮忙的吗？" },
            new() { Name = "午饭",      Hour = 12, Minute = 0,  Action = "eat",    Icon = "", Speech = "午饭时间到~记得吃点好的！" },
            new() { Name = "下午工作",  Hour = 14, Minute = 0,  Action = "work",   Icon = "", Speech = "下午继续努力~有什么代码需要写吗？" },
            new() { Name = "休息",      Hour = 17, Minute = 0,  Action = "rest",   Icon = "", Speech = "工作辛苦了！休息一下吧~" },
            new() { Name = "晚饭",      Hour = 19, Minute = 0,  Action = "eat",    Icon = "", Speech = "晚饭时间！要好好吃饭哦。" },
            new() { Name = "睡觉",      Hour = 23, Minute = 0,  Action = "sleep",  Icon = "", Speech = "晚安~明天见！做个好梦。" },
        };

        public IReadOnlyList<ScheduleSlot> Slots => _slots.AsReadOnly();

        public ScheduleManager(Main plugin, MemoryManager memory)
        {
            _plugin = plugin;
            _memory = memory;
            _slots = DefaultSchedule();
        }

        /// <summary>启动定时器</summary>
        public void Start()
        {
            _timer?.Dispose();
            _timer = new Timer(async _ => await OnTick(), null,
                TimeSpan.FromSeconds(60 - DateTime.Now.Second), // 整分对齐
                TimeSpan.FromMinutes(1));
        }

        /// <summary>停止</summary>
        public void Stop() => _timer?.Dispose();

        private async Task OnTick()
        {
            var now = DateTime.Now;
            var key = $"{now:yyyy-MM-dd}"; // 每日重置

            // 每天重置触发记录
            if (_todayTriggered.Count > 0 && !_todayTriggered.First().StartsWith(key))
                _todayTriggered.Clear();

            foreach (var slot in _slots)
            {
                if (!slot.IsTime(now)) continue;
                if (_todayTriggered.Contains($"{key}-{slot.Name}")) continue;

                _todayTriggered.Add($"{key}-{slot.Name}");
                await TriggerSlot(slot);
                break; // 一次只触发一个
            }
        }

        private async Task TriggerSlot(ScheduleSlot slot)
        {
            try
            {
                // 1. 说话
                var speech = $"{slot.Icon} {slot.Speech}";
                _plugin.MW.Dispatcher.Invoke(() =>
                    _plugin.MW.Main.Say(speech));

                // 2. 记录记忆
                _memory.AddFact("schedule", $"在 {DateTime.Now:HH:mm} 执行了「{slot.Name}」");

                // 3. 触发 VPet 动作（如可用）
                await TriggerPetAction(slot.Action);
            }
            catch { /* 不影响主循环 */ }
        }

        /// <summary>触发桌宠原生动作</summary>
        private async Task TriggerPetAction(string action)
        {
            try
            {
                var main = _plugin.MW.Main;
                switch (action)
                {
                    case "wakeup":
                        main.DisplaySleep(false);
                        main.State = VPet_Simulator.Core.Main.WorkingState.Nomal;
                        main.DisplayDefault();
                        break;

                    case "work":
                        // 找第一个适合的工作开始
                        main.WorkList(out var works, out var studies, out var _);
                        var job = works?.FirstOrDefault() ?? studies?.FirstOrDefault();
                        if (job != null && main.State != VPet_Simulator.Core.Main.WorkingState.Work)
                        {
                            main.StartWork(job);
                        }
                        break;

                    case "eat":
                        // 触发进食动画或说话
                        main.Say("🍚 开动啦~");
                        break;

                    case "rest":
                        main.Say("🎮 休息一会儿~");
                        break;

                    case "sleep":
                        // 先彻底停工作，再睡觉
                        if (main.State == VPet_Simulator.Core.Main.WorkingState.Work)
                        {
                            try { main.WorkTimer?.GetType().GetMethod("Stop")?.Invoke(main.WorkTimer, new object?[] { null, "schedule_sleep" }); } catch { }
                            main.State = VPet_Simulator.Core.Main.WorkingState.Nomal;
                            main.DisplayDefault();
                        }
                        main.DisplaySleep(true);
                        break;
                }
            }
            catch { }
            await Task.CompletedTask;
        }

        /// <summary>通过 AI 修改作息</summary>
        public void UpdateSlot(string name, int hour, int minute, string? speech = null)
        {
            var slot = _slots.FirstOrDefault(s =>
                s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (slot == null) return;

            slot.Hour = hour;
            slot.Minute = minute;
            if (!string.IsNullOrWhiteSpace(speech))
                slot.Speech = speech;

            _memory.AddFact("schedule", $"修改了「{slot.Name}」时间为 {hour:D2}:{minute:D2}");
        }

        /// <summary>重置为默认作息</summary>
        public void ResetToDefault()
        {
            _slots = DefaultSchedule();
            _memory.AddFact("schedule", "作息已恢复为默认设置");
        }

        /// <summary>获取作息上下文（注入提示词）</summary>
        public string GetScheduleContext()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n===== 我的作息 =====");
            foreach (var s in _slots)
            {
                sb.AppendLine($"  {s.Hour:D2}:{s.Minute:D2} {s.Name}");
            }
            sb.AppendLine("到时间我会自动提醒你并执行对应动作。");
            sb.AppendLine("你可以告诉我修改作息，比如「把午饭调到12:30」");
            return sb.ToString();
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
