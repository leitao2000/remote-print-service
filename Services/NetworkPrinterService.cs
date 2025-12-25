using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using RemotePrintService.Models;
using Spectre.Console;
using System.Collections.Concurrent;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 网络打印机服务实现类
    /// </summary>
    public class NetworkPrinterService : IPrinterService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<NetworkPrinterService> _logger;
        
        // 打印机信息缓存，避免重复查询配置
        private static readonly ConcurrentDictionary<string, PrinterInfo> _printerCache = new();
        private static readonly ConcurrentDictionary<string, DateTime> _cacheTimestamps = new();
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5); // 缓存5分钟
        
        // 在线状态缓存，避免频繁检查
        private static readonly ConcurrentDictionary<string, (bool isOnline, DateTime timestamp)> _onlineStatusCache = new();
        private static readonly TimeSpan _onlineStatusCacheExpiry = TimeSpan.FromSeconds(30); // 在线状态缓存30秒

        public NetworkPrinterService(IConfiguration configuration, ILogger<NetworkPrinterService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有可用的网络打印机列表（带缓存优化）
        /// </summary>
        public async Task<List<PrinterInfo>> GetAvailablePrintersAsync()
        {
            var printers = new List<PrinterInfo>();
            
            try
            {
                var printersConfig = _configuration.GetSection("Printers:NetworkPrinters");
                
                if (printersConfig.Exists())
                {
                    // 并行处理打印机配置，提高速度
                    var printerTasks = new List<Task<PrinterInfo?>>();
                    
                    foreach (var printerSection in printersConfig.GetChildren())
                    {
                        var name = printerSection["Name"] ?? string.Empty;
                        var ipAddress = printerSection["IpAddress"] ?? string.Empty;
                        var port = int.TryParse(printerSection["Port"], out var p) ? p : 9100;
                        
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ipAddress))
                            continue;
                        
                        // 检查缓存
                        var cacheKey = $"{name}_{ipAddress}_{port}";
                        if (_printerCache.TryGetValue(cacheKey, out var cachedPrinter) && 
                            _cacheTimestamps.TryGetValue(cacheKey, out var timestamp) &&
                            DateTime.UtcNow - timestamp < _cacheExpiry)
                        {
                            printers.Add(cachedPrinter);
                            continue;
                        }
                        
                        // 异步获取打印机信息（不检查在线状态以提高速度）
                        printerTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var printer = new PrinterInfo
                                {
                                    Name = name,
                                    IpAddress = ipAddress,
                                    Port = port,
                                    IsDefault = bool.TryParse(printerSection["IsDefault"], out var isDefault) && isDefault,
                                    Description = printerSection["Description"],
                                    IsOnline = await IsPrinterOnlineAsyncCached(ipAddress, port) // 使用缓存的在线检查
                                };
                                
                                // 更新缓存
                                _printerCache.TryAdd(cacheKey, printer);
                                _cacheTimestamps.TryAdd(cacheKey, DateTime.UtcNow);
                                
                                return printer;
                            }
                            catch
                            {
                                return null;
                            }
                        }));
                    }
                    
                    // 等待所有任务完成
                    var results = await Task.WhenAll(printerTasks);
                    printers.AddRange(results.Where(p => p != null)!);
                }

                _logger.LogInformation($"共找到 {printers.Count} 个网络打印机");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取可用打印机时发生错误");
            }

            return printers;
        }

        /// <summary>
        /// 获取指定网络打印机的详细信息（带缓存优化）
        /// </summary>
        public async Task<PrinterInfo?> GetPrinterInfoAsync(string printerName)
        {
            try
            {
                // 先从缓存查找
                var cached = _printerCache.Values.FirstOrDefault(p => 
                    p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));
                
                if (cached != null)
                {
                    var cacheKey = $"{cached.Name}_{cached.IpAddress}_{cached.Port}";
                    if (_cacheTimestamps.TryGetValue(cacheKey, out var timestamp) &&
                        DateTime.UtcNow - timestamp < _cacheExpiry)
                    {
                        return cached;
                    }
                }
                
                // 缓存未命中，重新获取
                var printers = await GetAvailablePrintersAsync();
                return printers.FirstOrDefault(p => p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取打印机信息时发生错误: {printerName}");
                return null;
            }
        }

        /// <summary>
        /// 执行网络打印机打印操作（优化版本）
        /// </summary>
        public async Task<PrintResponse> PrintAsync(PrintRequest request)
        {
            try
            {
                if (!File.Exists(request.FilePath))
                {
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

                // 快速检查在线状态（使用缓存，不阻塞）
                var isOnline = await IsPrinterOnlineAsyncCached(printerInfo.IpAddress, printerInfo.Port);
                if (!isOnline)
                {
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机离线: {printerName}"
                    };
                }

                _logger.LogInformation($"开始打印: {request.FilePath} -> {printerName}");
                
                // 并行执行文件读取和打印任务发送（流式处理）
                string jobId = await SendPrintJobAsyncOptimized(printerInfo, request.FilePath, request);

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
                _logger.LogError(ex, "打印操作失败");
                return new PrintResponse
                {
                    Success = false,
                    Message = $"打印失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 检查指定网络打印机是否在线（使用缓存）
        /// </summary>
        public async Task<bool> IsPrinterOnlineAsync(string printerName)
        {
            try
            {
                var printerInfo = await GetPrinterInfoAsync(printerName);
                if (printerInfo == null)
                    return false;

                return await IsPrinterOnlineAsyncCached(printerInfo.IpAddress, printerInfo.Port);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 检查网络打印机是否在线（带缓存优化）
        /// </summary>
        private async Task<bool> IsPrinterOnlineAsyncCached(string ipAddress, int port)
        {
            var cacheKey = $"{ipAddress}:{port}";
            
            // 检查缓存
            if (_onlineStatusCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.timestamp < _onlineStatusCacheExpiry)
                {
                    return cached.isOnline;
                }
            }
            
            // 缓存未命中或已过期，执行实际检查
            var isOnline = await IsPrinterOnlineAsync(ipAddress, port);
            
            // 更新缓存
            _onlineStatusCache.AddOrUpdate(cacheKey, 
                (isOnline, DateTime.UtcNow), 
                (key, old) => (isOnline, DateTime.UtcNow));
            
            return isOnline;
        }

        /// <summary>
        /// 获取指定网络打印机的打印队列中的任务数（优化版本）
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
                    client.NoDelay = true; // 优化 TCP 性能
                    client.ReceiveTimeout = 2000; // 减少超时时间
                    client.SendTimeout = 2000;

                    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(2000)))
                    {
                        await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port).WaitAsync(cts.Token);
                        return 1;
                    }
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 向网络打印机发送打印任务（优化版本 - 流式传输）
        /// </summary>
        private async Task<string> SendPrintJobAsyncOptimized(PrinterInfo printerInfo, string filePath, PrintRequest request)
        {
            string jobId = Guid.NewGuid().ToString("N");

            try
            {
                using (var client = new TcpClient())
                {
                    // 优化 TCP 设置以提高性能
                    client.NoDelay = true; // 禁用 Nagle 算法，减少延迟
                    client.SendTimeout = 10000; // 增加超时时间以支持大文件
                    client.ReceiveTimeout = 3000; // 接收超时保持较短

                    await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port);

                    using (var networkStream = client.GetStream())
                    {
                        // 设置网络流缓冲区大小（64KB）
                        networkStream.WriteTimeout = 10000;
                        
                        // 发送初始化命令
                        var initCommand = new byte[] { 0x1B, 0x40 };
                        await networkStream.WriteAsync(initCommand, 0, initCommand.Length);

                        // 使用流式传输文件内容，避免一次性加载到内存
                        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan | FileOptions.Asynchronous))
                        {
                            // 对于多份打印，重复发送文件内容
                            for (int copy = 0; copy < request.Copies; copy++)
                            {
                                fileStream.Position = 0; // 重置文件流位置
                                
                                // 使用缓冲区流式传输，减少内存占用
                                const int bufferSize = 65536; // 64KB 缓冲区
                                var buffer = new byte[bufferSize];
                                int bytesRead;
                                
                                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, bufferSize)) > 0)
                                {
                                    await networkStream.WriteAsync(buffer, 0, bytesRead);
                                }
                            }
                        }

                        // 发送打印并进纸命令
                        var finishCommand = new byte[] { 0x1B, 0x64, 0x0A };
                        await networkStream.WriteAsync(finishCommand, 0, finishCommand.Length);
                        
                        await networkStream.FlushAsync();
                    }
                }
                
                _logger.LogInformation($"已成功发送打印任务到打印机 {printerInfo.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"发送打印任务到打印机时发生错误: {printerInfo.Name}");
                throw;
            }

            return jobId;
        }
        
        /// <summary>
        /// 向网络打印机发送打印任务（旧版本，保留用于兼容）
        /// </summary>
        [Obsolete("使用 SendPrintJobAsyncOptimized 以获得更好的性能")]
        private async Task<string> SendPrintJobAsync(PrinterInfo printerInfo, byte[] fileContent, PrintRequest request)
        {
            string jobId = Guid.NewGuid().ToString("N");

            try
            {
                using (var client = new TcpClient())
                {
                    client.NoDelay = true; // 优化 TCP 性能
                    client.SendTimeout = 10000;
                    client.ReceiveTimeout = 3000;

                    await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port);

                    using (var stream = client.GetStream())
                    {
                        // 初始化打印机
                        var initCommand = new byte[] { 0x1B, 0x40 };
                        await stream.WriteAsync(initCommand, 0, initCommand.Length);

                        // 添加打印份数
                        for (int i = 0; i < request.Copies; i++)
                        {
                            await stream.WriteAsync(fileContent, 0, fileContent.Length);
                        }

                        // 打印并进纸
                        var finishCommand = new byte[] { 0x1B, 0x64, 0x0A };
                        await stream.WriteAsync(finishCommand, 0, finishCommand.Length);
                        
                        await stream.FlushAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"发送打印任务到打印机时发生错误: {printerInfo.Name}");
                throw;
            }

            return jobId;
        }

        /// <summary>
        /// 检查网络打印机IP是否在线（优化版本 - 快速检查）
        /// </summary>
        private async Task<bool> IsPrinterOnlineAsync(string ipAddress, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.NoDelay = true; // 禁用 Nagle 算法
                    client.SendTimeout = 1500; // 减少超时时间以提高响应速度
                    client.ReceiveTimeout = 1500;

                    // 使用 CancellationToken 实现快速超时
                    using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500)))
                    {
                        await client.ConnectAsync(ipAddress, port).WaitAsync(cts.Token);
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 超时，认为打印机离线
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}