namespace RemotePrintService.Models
{
    /// <summary>
    /// 打印响应模型
    /// 用于封装打印操作的执行结果
    /// 包含操作成功状态、消息、任务ID等响应信息
    /// </summary>
    public class PrintResponse
    {
        /// <summary>
        /// 是否成功
        /// 表示打印操作是否成功执行
        /// true: 操作成功
        /// false: 操作失败
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// 包含操作结果的详细描述信息
        /// 成功时返回成功消息，失败时返回错误原因
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 打印任务ID
        /// 唯一标识本次打印任务的字符串
        /// 可用于跟踪和查询打印任务状态
        /// </summary>
        public string? JobId { get; set; }

        /// <summary>
        /// 时间戳
        /// 记录响应生成的时间点
        /// 使用UTC时间格式
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 错误代码
        /// 用于标识错误类型，便于程序化处理
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// 错误详细信息
        /// 包含详细的错误信息，主要用于日志记录和调试
        /// </summary>
        public string? ErrorDetails { get; set; }

        /// <summary>
        /// 重试次数
        /// 记录操作重试的次数
        /// </summary>
        public int RetryCount { get; set; }
    }
}