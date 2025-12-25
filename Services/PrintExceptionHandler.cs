using RemotePrintService.Models;
using System.Net.Sockets;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 打印异常处理服务
    /// </summary>
    public static class PrintExceptionHandler
    {
        /// <summary>
        /// 处理异常并生成友好的错误消息
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="operation">操作描述</param>
        /// <returns>打印响应</returns>
        public static PrintResponse HandleException(Exception ex, string operation = "打印操作")
        {
            var errorCode = GetErrorCode(ex);
            var errorMessage = GetErrorMessage(ex, operation);

            return new PrintResponse
            {
                Success = false,
                Message = errorMessage,
                ErrorCode = errorCode,
                ErrorDetails = GetErrorDetails(ex)
            };
        }

        /// <summary>
        /// 获取错误代码
        /// </summary>
        private static string GetErrorCode(Exception ex)
        {
            return ex switch
            {
                FileNotFoundException => "FILE_NOT_FOUND",
                UnauthorizedAccessException => "ACCESS_DENIED",
                IOException => "IO_ERROR",
                SocketException => "NETWORK_ERROR",
                TimeoutException => "TIMEOUT",
                ArgumentException => "INVALID_ARGUMENT",
                InvalidOperationException => "INVALID_OPERATION",
                _ => "UNKNOWN_ERROR"
            };
        }

        /// <summary>
        /// 获取友好的错误消息
        /// </summary>
        private static string GetErrorMessage(Exception ex, string operation)
        {
            return ex switch
            {
                FileNotFoundException fileEx => $"文件不存在: {fileEx.FileName}",
                UnauthorizedAccessException => "没有权限访问文件或打印机",
                IOException ioEx => $"文件操作失败: {ioEx.Message}",
                SocketException socketEx => GetSocketErrorMessage(socketEx),
                TimeoutException => "操作超时，请检查网络连接或打印机状态",
                ArgumentException argEx => $"参数错误: {argEx.Message}",
                InvalidOperationException opEx => $"操作无效: {opEx.Message}",
                _ => $"{operation}失败: {ex.Message}"
            };
        }

        /// <summary>
        /// 获取Socket异常的错误消息
        /// </summary>
        private static string GetSocketErrorMessage(SocketException ex)
        {
            return ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "打印机拒绝连接，请检查打印机是否在线",
                SocketError.TimedOut => "连接超时，请检查网络连接",
                SocketError.HostUnreachable => "无法访问打印机，请检查网络配置",
                SocketError.NetworkUnreachable => "网络不可达，请检查网络连接",
                SocketError.ConnectionReset => "连接被重置，请重试",
                SocketError.AddressAlreadyInUse => "地址已被使用",
                _ => $"网络错误: {ex.Message} (错误代码: {ex.SocketErrorCode})"
            };
        }

        /// <summary>
        /// 获取错误详细信息（用于日志）
        /// </summary>
        private static string GetErrorDetails(Exception ex)
        {
            var details = new System.Text.StringBuilder();
            details.AppendLine($"异常类型: {ex.GetType().Name}");
            details.AppendLine($"错误消息: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                details.AppendLine($"内部异常: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
            }

            if (ex is SocketException socketEx)
            {
                details.AppendLine($"Socket错误代码: {socketEx.SocketErrorCode}");
                details.AppendLine($"错误代码: {socketEx.ErrorCode}");
            }

            details.AppendLine($"堆栈跟踪: {ex.StackTrace}");

            return details.ToString();
        }

        /// <summary>
        /// 判断异常是否可重试
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <returns>是否可重试</returns>
        public static bool IsRetryable(Exception ex)
        {
            return ex switch
            {
                SocketException socketEx => socketEx.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => true,
                    SocketError.TimedOut => true,
                    SocketError.ConnectionReset => true,
                    SocketError.NetworkUnreachable => true,
                    _ => false
                },
                TimeoutException => true,
                IOException => true,
                _ => false
            };
        }

        /// <summary>
        /// 获取重试延迟时间（毫秒）
        /// </summary>
        /// <param name="attempt">当前尝试次数</param>
        /// <returns>延迟时间（毫秒）</returns>
        public static int GetRetryDelay(int attempt)
        {
            // 指数退避策略：第1次重试等待1秒，第2次2秒，第3次4秒
            return (int)Math.Pow(2, attempt - 1) * 1000;
        }
    }
}

