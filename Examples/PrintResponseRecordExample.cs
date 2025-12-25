namespace RemotePrintService.Examples
{
    /// <summary>
    /// 演示如何将 PrintResponse 改为 record 类型以支持 with 表达式
    /// </summary>
    
    // ========== 方案 1: 使用 record（推荐，不可变）==========
    public record PrintResponseRecord
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? JobId { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string? ErrorCode { get; init; }
        public string? ErrorDetails { get; init; }
        public int RetryCount { get; init; }
    }

    // ========== 方案 2: 使用 record struct（值类型）==========
    public record struct PrintResponseRecordStruct
    {
        public bool Success { get; init; }
        public string Message { get; init; }
        public string? JobId { get; init; }
        public DateTime Timestamp { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorDetails { get; init; }
        public int RetryCount { get; init; }
    }

    /// <summary>
    /// 使用示例
    /// </summary>
    public class PrintResponseUsageExample
    {
        public static void Example()
        {
            // 创建初始响应
            var response = new PrintResponseRecord
            {
                Success = true,
                Message = "打印成功",
                JobId = "job-123",
                Timestamp = DateTime.UtcNow
            };

            // 使用 with 表达式添加重试次数（不修改原对象，创建新对象）
            var responseWithRetry = response with { RetryCount = 3 };
            
            // 使用 with 表达式修改为错误响应
            var errorResponse = response with
            {
                Success = false,
                Message = "打印失败",
                ErrorCode = "PRINTER_OFFLINE",
                RetryCount = 2
            };

            // 可以链式使用多个 with
            var finalResponse = response
                with { RetryCount = 1 }
                with { ErrorCode = "TIMEOUT" };

            // 注意：record 是不可变的，每次 with 都创建新对象
            Console.WriteLine($"原始: {response.RetryCount}"); // 0
            Console.WriteLine($"修改后: {responseWithRetry.RetryCount}"); // 3
            Console.WriteLine($"原始未变: {response.RetryCount}"); // 0（原对象未改变）
        }

        /// <summary>
        /// 在异常处理中使用
        /// </summary>
        public static PrintResponseRecord HandleExceptionWithRecord(Exception ex, int retryCount)
        {
            var baseResponse = new PrintResponseRecord
            {
                Success = false,
                Message = ex.Message,
                ErrorCode = "UNKNOWN_ERROR",
                Timestamp = DateTime.UtcNow
            };

            // 使用 with 表达式添加重试次数和详细信息
            return baseResponse with
            {
                RetryCount = retryCount,
                ErrorDetails = ex.ToString()
            };
        }
    }
}

