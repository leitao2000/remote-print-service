using RemotePrintService.Models;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 打印请求验证服务
    /// </summary>
    public static class PrintValidationService
    {
        /// <summary>
        /// 验证打印请求
        /// </summary>
        /// <param name="request">打印请求</param>
        /// <returns>验证结果，如果验证通过返回null，否则返回错误消息</returns>
        public static string? ValidatePrintRequest(PrintRequest? request)
        {
            if (request == null)
            {
                return "打印请求不能为空";
            }

            // 验证文件路径
            if (string.IsNullOrWhiteSpace(request.FilePath))
            {
                return "文件路径不能为空";
            }

            // 验证文件是否存在
            if (!File.Exists(request.FilePath))
            {
                return $"文件不存在: {request.FilePath}";
            }

            // 验证文件大小（最大500MB）
            try
            {
                var fileInfo = new FileInfo(request.FilePath);
                const long maxFileSize = 500 * 1024 * 1024; // 500MB
                if (fileInfo.Length > maxFileSize)
                {
                    return $"文件大小超过限制: {fileInfo.Length / 1024 / 1024}MB，最大允许 {maxFileSize / 1024 / 1024}MB";
                }

                if (fileInfo.Length == 0)
                {
                    return "文件为空，无法打印";
                }
            }
            catch (Exception ex)
            {
                return $"无法访问文件: {ex.Message}";
            }

            // 验证打印份数
            if (request.Copies < 1)
            {
                return "打印份数必须大于0";
            }

            if (request.Copies > 100)
            {
                return "打印份数不能超过100份";
            }

            // 验证打印方向
            if (!string.IsNullOrWhiteSpace(request.Orientation))
            {
                var orientation = request.Orientation.ToLower();
                if (orientation != "portrait" && orientation != "landscape")
                {
                    return $"打印方向无效: {request.Orientation}，必须是 Portrait 或 Landscape";
                }
            }

            // 验证文件扩展名
            var extension = Path.GetExtension(request.FilePath).ToLower();
            var allowedExtensions = new[] { ".pdf", ".txt", ".prn", ".ps", ".pcl" };
            if (!string.IsNullOrEmpty(extension) && !allowedExtensions.Contains(extension))
            {
                return $"不支持的文件格式: {extension}，支持的格式: {string.Join(", ", allowedExtensions)}";
            }

            return null; // 验证通过
        }

        /// <summary>
        /// 验证打印机名称
        /// </summary>
        /// <param name="printerName">打印机名称</param>
        /// <returns>验证结果</returns>
        public static string? ValidatePrinterName(string? printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName))
            {
                return null; // 允许为空，将使用默认打印机
            }

            // 验证打印机名称长度
            if (printerName.Length > 255)
            {
                return "打印机名称长度不能超过255个字符";
            }

            // 验证打印机名称中不包含非法字符
            var invalidChars = Path.GetInvalidFileNameChars();
            if (printerName.Any(c => invalidChars.Contains(c)))
            {
                return $"打印机名称包含非法字符";
            }

            return null; // 验证通过
        }

        /// <summary>
        /// 验证IP地址和端口
        /// </summary>
        /// <param name="ipAddress">IP地址</param>
        /// <param name="port">端口</param>
        /// <returns>验证结果</returns>
        public static string? ValidateNetworkAddress(string ipAddress, int port)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return "IP地址不能为空";
            }

            // 验证IP地址格式
            if (!System.Net.IPAddress.TryParse(ipAddress, out _))
            {
                return $"无效的IP地址格式: {ipAddress}";
            }

            // 验证端口范围
            if (port < 1 || port > 65535)
            {
                return $"端口号必须在1-65535之间: {port}";
            }

            return null; // 验证通过
        }
    }
}

