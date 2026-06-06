using System.Diagnostics;
using System.IO;
using VPet.Plugin.MeiChat.Memory;

namespace VPet.Plugin.MeiChat.Schedule
{
    /// <summary>
    /// 主动互动模块 — 桌宠会定期观察环境、主动说话
    /// 比如发现你在写代码、浏览网页、长时间没互动等
    /// </summary>
    public class ProactiveInteraction : IDisposable
    {
        private readonly Main _plugin;
        private readonly MemoryManager _memory;
        private Timer? _timer;
        private readonly Random _rng = new();
        private DateTime _lastInteraction = DateTime.MinValue;
        private int _checkCount;

        // 检测到的活动缓存
        private string _lastDetectedActivity = "";

        private static readonly string[] MorningGreetings =
        {
            "早上好呀~今天也是充满干劲的一天呢！",
            "早安！昨晚睡得好吗？",
            "清晨的阳光真不错，今天想做什么呢？",
        };

        private static readonly string[] AfternoonGreetings =
        {
            "下午好~工作还顺利吗？",
            "有没有什么需要我帮忙的？",
            "累的话记得起来活动一下哦~",
        };

        private static readonly string[] EveningGreetings =
        {
            "晚上好~今天过得怎么样？",
            "辛苦了！需要我帮你做点什么吗？",
            "天黑了，要不要开灯呀？",
        };

        private static readonly string[] NightGreetings =
        {
            "已经很晚了，还不睡吗？",
            "熬夜对身体不好哦，早点休息吧~",
            "夜深了，要我陪你一会儿吗？",
        };

        private static readonly string[] IdleMessages =
        {
            "唔…好无聊呀，跟我说说话嘛~",
            "我在这里哦，有什么想聊的吗？",
            "一直盯着屏幕看，眼睛不累吗？",
        };

        private static readonly string[] CodingDetected =
        {
            "在写代码呀？需要我帮忙 review 一下吗？",
            "哇，又在写代码！需要测试助手吗？",
            "代码写得怎么样了？有报错的话我可以帮你看~",
        };

        private static readonly string[] BrowserDetected =
        {
            "在查资料吗？要我帮你总结一下吗？",
            "在看什么呢？能跟我说说吗？",
            "刷到有趣的东西了？分享一下嘛~",
        };

        private static readonly string[] GameDetected =
        {
            "在玩游戏呀？玩开心！",
            "游戏时间到~不过记得适可而止哦！",
            "要不要我给你加油打气？",
        };

        private static readonly string[] WorkTimeMessages =
        {
            "工作时间！加油加油~",
            "专注工作的时间到了，有什么需要我帮忙的尽管说！",
            "要开始干活了，我先安静待着，有事叫我~",
        };

        private static readonly string[] RestTimeMessages =
        {
            "休息时间到！起来走走喝杯水吧~",
            "该休息啦，一直坐着对身体不好哦！",
            "休息一会儿吧，我可以陪你说说话~",
        };

        public ProactiveInteraction(Main plugin, MemoryManager memory)
        {
            _plugin = plugin;
            _memory = memory;
        }

        /// <summary>启动主动互动（每 3 分钟检查一次）</summary>
        public void Start()
        {
            _timer?.Dispose();
            _timer = new Timer(_ => OnCheck(), null,
                TimeSpan.FromMinutes(2),  // 首次 2 分钟后
                TimeSpan.FromMinutes(3)); // 之后每 3 分钟
        }

        public void Stop() => _timer?.Dispose();

        /// <summary>记录用户互动时间（TalkBox 调用）</summary>
        public void NotifyInteraction() => _lastInteraction = DateTime.Now;

        private void OnCheck()
        {
            try
            {
                _checkCount++;

                // 距离上次互动 < 5 分钟，不打扰
                if ((DateTime.Now - _lastInteraction).TotalMinutes < 5)
                    return;

                // 桌宠正在睡觉或工作中，不说话
                if (_plugin.MW.Main.State == VPet_Simulator.Core.Main.WorkingState.Sleep ||
                    _plugin.MW.Main.State == VPet_Simulator.Core.Main.WorkingState.Work)
                    return;

                // 检测环境
                var activity = DetectActivity();
                var hour = DateTime.Now.Hour;

                // 选一句合适的话
                var message = PickMessage(activity, hour);
                if (string.IsNullOrEmpty(message)) return;

                // 桌宠说话（加上表情前缀）
                var final = GetRandomEmoji() + " " + message;

                _plugin.MW.Dispatcher.Invoke(() =>
                {
                    _plugin.MW.Main.Say(final);
                });

                _lastInteraction = DateTime.Now;
                _memory?.AddFact("interaction", $"在 {DateTime.Now:HH:mm} 主动说了：{message}");
            }
            catch { /* 主动互动失败不影响主程序 */ }
        }

        /// <summary>检测用户当前在做什么</summary>
        private string DetectActivity()
        {
            try
            {
                var processes = Process.GetProcesses();
                bool hasCode = false, hasBrowser = false, hasGame = false;

                foreach (var p in processes)
                {
                    try
                    {
                        var name = p.ProcessName.ToLower();
                        if (name.Contains("code") || name.Contains("devenv") ||
                            name.Contains("idea") || name.Contains("sublime") ||
                            name.Contains("notepad++"))
                            hasCode = true;
                        else if (name.Contains("chrome") || name.Contains("edge") ||
                                 name.Contains("firefox") || name.Contains("opera") ||
                                 name.Contains("brave"))
                            hasBrowser = true;
                        else if (name.Contains("steam") || name.Contains("epic") ||
                                 name.Contains("league") || name.Contains("wow") ||
                                 name.Contains("genshin") || name.Contains("starrail") ||
                                 name.Contains("honkai") || name.Contains("game"))
                            hasGame = true;
                    }
                    catch { continue; }
                }

                // 检测工作目录最近修改的文件
                bool recentFileChange = false;
                try
                {
                    var workDir = _plugin.GetWorkingDirectory();
                    if (Directory.Exists(workDir))
                    {
                        recentFileChange = Directory.GetFiles(workDir, "*.*", SearchOption.TopDirectoryOnly)
                            .Any(f => File.GetLastWriteTime(f) > DateTime.Now.AddMinutes(-10));
                    }
                }
                catch { }

                if (hasCode || recentFileChange) _lastDetectedActivity = "coding";
                else if (hasGame) _lastDetectedActivity = "gaming";
                else if (hasBrowser) _lastDetectedActivity = "browsing";
                else _lastDetectedActivity = "unknown";

                return _lastDetectedActivity;
            }
            catch
            {
                return "unknown";
            }
        }

        private string PickMessage(string activity, int hour)
        {
            // 时间问候（首次检测或隔了很久）
            if (_checkCount <= 2 || (DateTime.Now - _lastInteraction).TotalHours > 1)
            {
                if (hour is >= 5 and < 9) return PickRandom(MorningGreetings);
                if (hour is >= 9 and < 18) return PickRandom(AfternoonGreetings);
                if (hour is >= 18 and < 22) return PickRandom(EveningGreetings);
                return PickRandom(NightGreetings);
            }

            // 根据活动说话
            if (activity == "coding") return PickRandom(CodingDetected);
            if (activity == "browsing") return PickRandom(BrowserDetected);
            if (activity == "gaming") return PickRandom(GameDetected);

            // 根据作息时间说话
            try
            {
                var schedule = _plugin.Scheduler?.Slots;
                if (schedule != null)
                {
                    var now = DateTime.Now;
                    var currentSlot = schedule.FirstOrDefault(s => s.IsTime(now));
                    if (currentSlot != null)
                    {
                        if (currentSlot.Action == "work") return PickRandom(WorkTimeMessages);
                        if (currentSlot.Action == "rest") return PickRandom(RestTimeMessages);
                    }
                }
            }
            catch { }

            // 长时间没互动
            if ((DateTime.Now - _lastInteraction).TotalMinutes > 30)
                return PickRandom(IdleMessages);

            return ""; // 不到说话的时候
        }

        private static string PickRandom(string[] arr) =>
            arr[Random.Shared.Next(arr.Length)];

        private static string GetRandomEmoji()
        {
            string[] emojis = { "🌸", "✨", "💬", "👀", "🤔", "😊", "🎀", "~" };
            return emojis[Random.Shared.Next(emojis.Length)];
        }

        public void Dispose() => _timer?.Dispose();
    }
}
