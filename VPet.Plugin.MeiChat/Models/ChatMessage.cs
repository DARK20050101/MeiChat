using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VPet.Plugin.MeiChat.Models
{
    /// <summary>
    /// 聊天消息模型
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _content = string.Empty;
        private bool _isUser;
        private bool _isStreaming;
        private DateTime _timestamp;

        /// <summary>消息发送时间</summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(); }
        }

        /// <summary>消息内容 (Markdown格式)</summary>
        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(); }
        }

        /// <summary>是否为用户消息 (false = AI回复)</summary>
        public bool IsUser
        {
            get => _isUser;
            set { _isUser = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAI)); }
        }

        /// <summary>是否为 AI 消息</summary>
        public bool IsAI => !_isUser;

        /// <summary>是否正在流式接收中</summary>
        public bool IsStreaming
        {
            get => _isStreaming;
            set { _isStreaming = value; OnPropertyChanged(); }
        }

        /// <summary>格式化的时间戳文本</summary>
        public string TimeText => Timestamp.ToString("HH:mm:ss");

        /// <summary>角色标签</summary>
        public string RoleLabel => IsUser ? "我" : "芽衣";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
