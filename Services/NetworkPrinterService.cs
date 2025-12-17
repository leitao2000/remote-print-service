using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using RemotePrintService.Models;
using Spectre.Console;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 网络打印机服务实现类
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
        /// 获取所有可用的网络打印机列表
        /// </summary>
        public async Task<List<PrinterInfo>> GetAvailablePrintersAsync()
        {
            var printers = new List<PrinterInfo>();
            
            try
            {
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
                            // 使用 Spectre.Console 显示发现的打印机
                            AnsiConsole.MarkupLine($"[green]发现网络打印机[/]: [blue]{printer.Name}[/] ([yellow]{printer.IpAddress}:{printer.Port}[/])");
                        }
                    }
                }

                // 使用 Spectre.Console 显示统计信息
                AnsiConsole.MarkupLine($"[bold blue]共找到 {printers.Count} 个网络打印机[/]");
                _logger.LogInformation($"共找到 {printers.Count} 个网络打印机");
            }
            catch (Exception ex)
            {
                // 使用 Spectre.Console 显示错误信息
                AnsiConsole.MarkupLine($"[red]获取可用打印机时发生错误: {ex.Message}[/]");
                _logger.LogError(ex, "获取可用打印机时发生错误");
            }

            return printers;
        }

        /// <summary>
        /// 获取指定网络打印机的详细信息
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
                // 使用 Spectre.Console 显示错误信息
                AnsiConsole.MarkupLine($"[red]获取打印机信息时发生错误[/]: {printerName} - {ex.Message}");
                _logger.LogError(ex, $"获取打印机信息时发生错误: {printerName}");
                return null;
            }
        }

        /// <summary>
        /// 执行网络打印机打印操作
        /// </summary>
        public async Task<PrintResponse> PrintAsync(PrintRequest request)
        {
            try
            {
                if (!File.Exists(request.FilePath))
                {
                    // 使用 Spectre.Console 显示错误信息
                    AnsiConsole.MarkupLine($"[red]文件不存在[/]: {request.FilePath}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"文件不存在: {request.FilePath}"
                    };
                }

                string printerName = request.PrinterName ?? _configuration["Printers:DefaultPrinter"] ?? string.Empty;
                
                if (string.IsNullOrEmpty(printerName))
                {
                    var printers = await GetAvailablePrintersAsync();
                    var defaultPrinter = printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
                    
                    if (defaultPrinter == null)
                    {
                        // 使用 Spectre.Console 显示错误信息
                        AnsiConsole.MarkupLine($"[red]未找到可用的打印机[/]");
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
                    // 使用 Spectre.Console 显示错误信息
                    AnsiConsole.MarkupLine($"[red]打印机不存在[/]: {printerName}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机不存在: {printerName}"
                    };
                }

                if (!printerInfo.IsOnline)
                {
                    // 使用 Spectre.Console 显示错误信息
                    AnsiConsole.MarkupLine($"[red]打印机离线[/]: {printerName}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机离线: {printerName}"
                    };
                }

                // 使用 Spectre.Console 显示进度信息
                AnsiConsole.MarkupLine($"[blue]读取文件内容[/]: [yellow]{request.FilePath}[/]");
                byte[] fileContent = await File.ReadAllBytesAsync(request.FilePath);

                // 使用 Spectre.Console 显示进度信息
                AnsiConsole.MarkupLine($"[blue]发送打印任务到网络打印机[/]: [yellow]{printerName}[/] ([cyan]{printerInfo.IpAddress}:{printerInfo.Port}[/])");
                string jobId = await SendPrintJobAsync(printerInfo, fileContent, request);

                // 使用 Spectre.Console 显示成功信息
                AnsiConsole.MarkupLine($"[green]打印任务 {jobId} 已发送到打印机[/] [yellow]{printerName}[/]");
                _logger.LogInformation($"打印任务 {jobId} 已发送到打印机 {printerName}");

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
                // 使用 Spectre.Console 显示错误信息
                AnsiConsole.MarkupLine($"[red]打印操作失败: {ex.Message}[/]");
                _logger.LogError(ex, "打印操作失败");
                return new PrintResponse
                {
                    Success = false,
                    Message = $"打印失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 检查指定网络打印机是否在线
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
        /// 获取指定网络打印机的打印队列中的任务数
        /// </summary>
        public async Task<int> GetPrintQueueCountAsync(string printerName)
        {
            try
            {
                var printerInfo = await GetPrinterInfoAsync(printerName);
                if (printerInfo == null)
                    return 0;

                using (var client = new TcpClient())
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;

                    await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port);
                    return 1;
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 向网络打印机发送打印任务
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

                    // 使用 Spectre.Console 显示连接信息
                    AnsiConsole.MarkupLine($"[blue]连接到网络打印机[/]: [yellow]{printerInfo.IpAddress}:{printerInfo.Port}[/]");
                    await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port);

                    using (var stream = client.GetStream())
                    {
                        var commands = new List<byte>();

                        // 初始化打印机
                        commands.AddRange(new byte[] { 0x1B, 0x40 });

                        // 添加打印份数
                        for (int i = 0; i < request.Copies; i++)
                        {
                            commands.AddRange(fileContent);
                        }

                        // 打印并进纸
                        commands.AddRange(new byte[] { 0x1B, 0x64, 0x0A });

                        // 使用 Spectre.Console 显示发送信息
                        AnsiConsole.MarkupLine($"[blue]发送数据到打印机[/]: [cyan]{commands.Count}[/] 字节");
                        await stream.WriteAsync(commands.ToArray(), 0, commands.Count);
                        await stream.FlushAsync();

                        AnsiConsole.MarkupLine($"[green]已发送 {commands.Count} 字节到打印机[/] [yellow]{printerInfo.Name}[/]");
                        _logger.LogInformation($"已发送 {commands.Count} 字节到打印机 {printerInfo.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                // 使用 Spectre.Console 显示错误信息
                AnsiConsole.MarkupLine($"[red]发送打印任务到打印机时发生错误[/]: {printerInfo.Name} - {ex.Message}");
                _logger.LogError(ex, $"发送打印任务到打印机时发生错误: {printerInfo.Name}");
                throw;
            }

            return jobId;
        }

        /// <summary>
        /// 检查网络打印机IP是否在线
        /// </summary>
        private async Task<bool> IsPrinterOnlineAsync(string ipAddress, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.SendTimeout = 2000;
                    client.ReceiveTimeout = 2000;

                    // 使用 Spectre.Console 显示连接测试信息
                    AnsiConsole.MarkupLine($"[blue]测试打印机连接[/]: [yellow]{ipAddress}:{port}[/]");
                    await client.ConnectAsync(ipAddress, port);
                    AnsiConsole.MarkupLine($"[green]打印机在线[/]: [yellow]{ipAddress}:{port}[/]");
                    return true;
                }
            }
            catch
            {
                AnsiConsole.MarkupLine($"[red]打印机离线[/]: [yellow]{ipAddress}:{port}[/]");
                return false;
            }
        }
    }
}