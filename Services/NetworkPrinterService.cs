using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using RemotePrintService.Models;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 网络打印机服务实现
    /// </summary>
    public class NetworkPrinterService : IPrinterService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<NetworkPrinterService> _logger;

        public NetworkPrinterService(IConfiguration configuration, ILogger<NetworkPrinterService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有可用的打印机列表
        /// </summary>
        public async Task<List<PrinterInfo>> GetAvailablePrintersAsync()
        {
            var printers = new List<PrinterInfo>();
            
            try
            {
                // 从配置文件读取网络打印机列表
                var printersConfig = _configuration.GetSection("Printers:NetworkPrinters");
                
                if (printersConfig.Exists())
                {
                    foreach (var printerSection in printersConfig.GetChildren())
                    {
                        var printer = new PrinterInfo
                        {
                            Name = printerSection["Name"] ?? string.Empty,
                            IpAddress = printerSection["IpAddress"] ?? string.Empty,
                            Port = int.TryParse(printerSection["Port"], out var port) ? port : 9100,
                            IsDefault = bool.TryParse(printerSection["IsDefault"], out var isDefault) && isDefault,
                            Description = printerSection["Description"],
                            IsOnline = await IsPrinterOnlineAsync(printerSection["IpAddress"] ?? string.Empty, int.TryParse(printerSection["Port"], out var p) ? p : 9100)
                        };

                        if (!string.IsNullOrEmpty(printer.Name) && !string.IsNullOrEmpty(printer.IpAddress))
                        {
                            printers.Add(printer);
                        }
                    }
                }

                _logger.LogInformation($"Found {printers.Count} network printers");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available printers");
            }

            return printers;
        }

        /// <summary>
        /// 获取指定打印机的信息
        /// </summary>
        public async Task<PrinterInfo?> GetPrinterInfoAsync(string printerName)
        {
            try
            {
                var printers = await GetAvailablePrintersAsync();
                return printers.FirstOrDefault(p => p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting printer info for {printerName}");
                return null;
            }
        }

        /// <summary>
        /// 执行打印操作
        /// </summary>
        public async Task<PrintResponse> PrintAsync(PrintRequest request)
        {
            try
            {
                // 验证文件是否存在
                if (!File.Exists(request.FilePath))
                {
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"文件不存在: {request.FilePath}"
                    };
                }

                // 获取目标打印机
                string printerName = request.PrinterName ?? _configuration["Printers:DefaultPrinter"] ?? string.Empty;
                
                if (string.IsNullOrEmpty(printerName))
                {
                    var printers = await GetAvailablePrintersAsync();
                    var defaultPrinter = printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
                    
                    if (defaultPrinter == null)
                    {
                        return new PrintResponse
                        {
                            Success = false,
                            Message = "未找到可用的打印机"
                        };
                    }
                    
                    printerName = defaultPrinter.Name;
                }

                var printerInfo = await GetPrinterInfoAsync(printerName);
                if (printerInfo == null)
                {
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机不存在: {printerName}"
                    };
                }

                // 检查打印机是否在线
                if (!printerInfo.IsOnline)
                {
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机离线: {printerName}"
                    };
                }

                // 读取文件内容
                byte[] fileContent = await File.ReadAllBytesAsync(request.FilePath);

                // 建立网络连接并发送打印数据
                string jobId = await SendPrintJobAsync(printerInfo, fileContent, request);

                _logger.LogInformation($"Print job {jobId} sent to printer {printerName}");

                return new PrintResponse
                {
                    Success = true,
                    Message = $"打印任务已发送至 {printerName}",
                    JobId = jobId,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during print operation");
                return new PrintResponse
                {
                    Success = false,
                    Message = $"打印失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 检查打印机是否在线
        /// </summary>
        public async Task<bool> IsPrinterOnlineAsync(string printerName)
        {
            try
            {
                var printerInfo = await GetPrinterInfoAsync(printerName);
                if (printerInfo == null)
                    return false;

                return await IsPrinterOnlineAsync(printerInfo.IpAddress, printerInfo.Port);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取打印队列中的任务数
        /// </summary>
        public async Task<int> GetPrintQueueCountAsync(string printerName)
        {
            try
            {
                var printerInfo = await GetPrinterInfoAsync(printerName);
                if (printerInfo == null)
                    return 0;

                // 尝试连接打印机，连接成功说明有任务或打印机响应
                using (var client = new TcpClient())
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;

                    await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port);
                    return 1; // 简单实现，实际可返回真实队列数
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 向打印机发送打印任务
        /// </summary>
        private async Task<string> SendPrintJobAsync(PrinterInfo printerInfo, byte[] fileContent, PrintRequest request)
        {
            string jobId = Guid.NewGuid().ToString("N");

            try
            {
                using (var client = new TcpClient())
                {
                    client.SendTimeout = 5000;
                    client.ReceiveTimeout = 5000;

                    // 连接到打印机
                    await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port);

                    using (var stream = client.GetStream())
                    {
                        // 构建打印机命令（ESC/POS格式示例）
                        var commands = new List<byte>();

                        // 初始化打印机
                        commands.AddRange(new byte[] { 0x1B, 0x40 }); // ESC @

                        // 添加打印份数
                        for (int i = 0; i < request.Copies; i++)
                        {
                            commands.AddRange(fileContent);
                        }

                        // 打印并进纸
                        commands.AddRange(new byte[] { 0x1B, 0x64, 0x0A }); // ESC d (n)

                        // 发送数据
                        await stream.WriteAsync(commands.ToArray(), 0, commands.Count);
                        await stream.FlushAsync();

                        _logger.LogInformation($"Sent {commands.Count} bytes to printer {printerInfo.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending print job to printer {printerInfo.Name}");
                throw;
            }

            return jobId;
        }

        /// <summary>
        /// 检查打印机IP是否在线
        /// </summary>
        private async Task<bool> IsPrinterOnlineAsync(string ipAddress, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.SendTimeout = 2000;
                    client.ReceiveTimeout = 2000;

                    await client.ConnectAsync(ipAddress, port);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
