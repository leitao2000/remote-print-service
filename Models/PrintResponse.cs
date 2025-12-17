namespace RemotePrintService.Models
{
    /// <summary>
    /// 打印响应模型
    /// </summary>
    public class PrintResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 打印任务ID
        /// </summary>
        public string? JobId { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
