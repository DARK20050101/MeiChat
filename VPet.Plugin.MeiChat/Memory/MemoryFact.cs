namespace VPet.Plugin.MeiChat.Memory
{
    /// <summary>
    /// 一条记忆碎片
    /// </summary>
    public class MemoryFact
    {
        public string Type { get; set; } = "fact";   // preference | project | fact | schedule | user_info
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool Deleted { get; set; } = false;    // 软删除，AI 可反悔

        public string ToContextLine(int index) =>
            $"{index}. [{Type}] {Content}（{Timestamp:MM-dd HH:mm}）";
    }
}
