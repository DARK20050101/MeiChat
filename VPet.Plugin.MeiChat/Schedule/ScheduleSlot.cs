namespace VPet.Plugin.MeiChat.Schedule
{
    /// <summary>
    /// 一个作息时间段
    /// </summary>
    public class ScheduleSlot
    {
        public string Name { get; set; } = "";           // 名称，如 "起床"
        public int Hour { get; set; }                    // 小时 0-23
        public int Minute { get; set; }                  // 分钟 0-59
        public string Action { get; set; } = "";         // 动作标识: wakeup | work | eat | rest | sleep
        public string Speech { get; set; } = "";         // 触发时说的话
        public string Icon { get; set; } = "";           // 表情图标

        /// <summary>检查是否到时间</summary>
        public bool IsTime(DateTime now) =>
            now.Hour == Hour && now.Minute == Minute;
    }
}
