using System.Text.Json.Serialization;

namespace VPet.Plugin.MeiChat.Agent
{
    /// <summary>
    /// API 使用统计 — 追踪 token 消耗和缓存命中率
    /// </summary>
    public class ApiStats
    {
        private readonly object _lock = new();

        [JsonIgnore]
        public int TotalPromptTokens { get; private set; }
        [JsonIgnore]
        public int TotalCompletionTokens { get; private set; }
        [JsonIgnore]
        public int CacheHitTokens { get; private set; }
        [JsonIgnore]
        public int CacheMissTokens { get; private set; }
        [JsonIgnore]
        public int RequestCount { get; private set; }
        [JsonIgnore]
        public int TotalTokens => TotalPromptTokens + TotalCompletionTokens;

        [JsonIgnore]
        public double CacheHitRate =>
            CacheHitTokens + CacheMissTokens > 0
                ? (double)CacheHitTokens / (CacheHitTokens + CacheMissTokens) * 100
                : 0;

        /// <summary>记录一次 API 调用的用量</summary>
        public void RecordUsage(int promptTokens, int completionTokens, int? cacheHit = null, int? cacheMiss = null)
        {
            lock (_lock)
            {
                RequestCount++;
                TotalPromptTokens += promptTokens;
                TotalCompletionTokens += completionTokens;
                if (cacheHit.HasValue) CacheHitTokens += cacheHit.Value;
                if (cacheMiss.HasValue) CacheMissTokens += cacheMiss.Value;
            }
        }

        /// <summary>获取统计摘要文本</summary>
        public string GetSummary()
        {
            lock (_lock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("📊 API 统计");
                sb.AppendLine($"请求次数: {RequestCount}");
                sb.AppendLine($"Prompt tokens: {TotalPromptTokens:N0}");
                sb.AppendLine($"Completion tokens: {TotalCompletionTokens:N0}");
                sb.AppendLine($"总消耗: {TotalTokens:N0} tokens");
                sb.AppendLine($"缓存命中率: {CacheHitRate:F1}%");
                if (RequestCount > 0)
                {
                    sb.AppendLine($"平均每次: {(TotalTokens / (double)RequestCount):N0} tokens");
                }
                return sb.ToString();
            }
        }
    }
}
