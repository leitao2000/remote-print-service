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
        /// 执行网络打印机打印操作（优化版本，带完善的错误校验和重试机制）
        /// </summary>
        public async Task<PrintResponse> PrintAsync(PrintRequest request)
        {
            const int maxRetries = 3; // 最大重试次数
            int retryCount = 0;
            Exception? lastException = null;

            try
            {
                // 1. 验证打印请求
                var validationError = PrintValidationService.ValidatePrintRequest(request);
                if (validationError != null)
                {
                    _logger.LogWarning($"打印请求验证失败: {validationError}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = validationError,
                        ErrorCode = "VALIDATION_ERROR"
                    };
                }

                // 2. 验证打印机名称
                var printerNameError = PrintValidationService.ValidatePrinterName(request.PrinterName);
                if (printerNameError != null)
                {
                    _logger.LogWarning($"打印机名称验证失败: {printerNameError}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = printerNameError,
                        ErrorCode = "INVALID_PRINTER_NAME"
                    };
                }

                // 3. 获取打印机信息
                string printerName = request.PrinterName ?? _configuration["Printers:DefaultPrinter"] ?? string.Empty;
                
                if (string.IsNullOrEmpty(printerName))
                {
                    var printers = await GetAvailablePrintersAsync();
                    var defaultPrinter = printers.FirstOrDefault(p => p.IsDefault) ?? printers.FirstOrDefault();
                    
                    if (defaultPrinter == null)
                    {
                        _logger.LogWarning("未找到可用的打印机");
                        return new PrintResponse
                        {
                            Success = false,
                            Message = "未找到可用的打印机",
                            ErrorCode = "NO_PRINTER_AVAILABLE"
                        };
                    }
                    
                    printerName = defaultPrinter.Name;
                }

                var printerInfo = await GetPrinterInfoAsync(printerName);
                if (printerInfo == null)
                {
                    _logger.LogWarning($"打印机不存在: {printerName}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机不存在: {printerName}",
                        ErrorCode = "PRINTER_NOT_FOUND"
                    };
                }

                // 4. 验证网络地址
                var addressError = PrintValidationService.ValidateNetworkAddress(printerInfo.IpAddress, printerInfo.Port);
                if (addressError != null)
                {
                    _logger.LogWarning($"网络地址验证失败: {addressError}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = addressError,
                        ErrorCode = "INVALID_NETWORK_ADDRESS"
                    };
                }

                // 5. 检查打印机在线状态（带重试机制）
                bool isOnline = false;
                for (int i = 0; i < 2; i++) // 最多检查2次
                {
                    isOnline = await IsPrinterOnlineAsyncCached(printerInfo.IpAddress, printerInfo.Port);
                    if (isOnline) break;
                    if (i < 1) await Task.Delay(500); // 等待500ms后重试
                }

                if (!isOnline)
                {
                    _logger.LogWarning($"打印机离线: {printerName} ({printerInfo.IpAddress}:{printerInfo.Port})");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机离线: {printerName}",
                        ErrorCode = "PRINTER_OFFLINE"
                    };
                }

                _logger.LogInformation($"开始打印: {request.FilePath} -> {printerName} (份数: {request.Copies})");

                // 6. 执行打印任务（优化为后台异步执行，立即返回响应）
                string jobId = Guid.NewGuid().ToString("N");
                
                // 在后台异步执行打印任务，立即返回响应
                _ = Task.Run(async () =>
                {
                    int localRetryCount = 0;
                    Exception? localLastException = null;
                    
                    while (localRetryCount <= maxRetries)
                    {
                        try
                        {
                            await SendPrintJobAsyncOptimized(printerInfo, request.FilePath, request);
                            _logger.LogInformation($"后台打印任务 {jobId} 已成功发送到打印机 {printerName} (重试次数: {localRetryCount})");
                            return; // 成功完成
                        }
                        catch (Exception ex)
                        {
                            localLastException = ex;
                            localRetryCount++;

                            // 判断是否可重试
                            if (localRetryCount > maxRetries || !PrintExceptionHandler.IsRetryable(ex))
                            {
                                _logger.LogError(ex, $"后台打印任务发送失败 (重试次数: {localRetryCount - 1}): {printerName}");
                                break;
                            }

                            // 计算重试延迟
                            var delay = PrintExceptionHandler.GetRetryDelay(localRetryCount);
                            _logger.LogWarning($"后台打印任务发送失败，{delay}ms 后重试 (第 {localRetryCount} 次): {ex.Message}");
                            await Task.Delay(delay);
                        }
                    }
                    
                    // 所有重试都失败，记录错误
                    if (localLastException != null)
                    {
                        _logger.LogError(localLastException, $"后台打印任务 {jobId} 最终失败");
                    }
                });

                // 立即返回响应，不等待打印完成
                _logger.LogInformation($"打印任务 {jobId} 已提交到后台队列，立即返回响应");
                
                return new PrintResponse
                {
                    Success = true,
                    Message = $"打印任务已提交至 {printerName}，正在后台处理",
                    JobId = jobId,
                    Timestamp = DateTime.UtcNow,
                    RetryCount = 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印操作发生未预期的错误");
                var errorResponse = PrintExceptionHandler.HandleException(ex, "打印操作");
                errorResponse.RetryCount = retryCount;
                return errorResponse;
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
        /// 向网络打印机发送打印任务（优化版本 - 流式传输，带完善的错误处理）
        /// </summary>
        private async Task<string> SendPrintJobAsyncOptimized(PrinterInfo printerInfo, string filePath, PrintRequest request)
        {
            string jobId = Guid.NewGuid().ToString("N");
            TcpClient? client = null;
            NetworkStream? networkStream = null;
            FileStream? fileStream = null;

            try
            {
                // 验证文件信息
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException($"文件不存在: {filePath}", filePath);
                }

                if (fileInfo.Length == 0)
                {
                    throw new InvalidOperationException("文件为空，无法打印");
                }

                _logger.LogInformation($"准备发送打印任务: 文件={filePath}, 大小={fileInfo.Length} 字节, 打印机={printerInfo.Name}");

                client = new TcpClient();
                
                // 优化 TCP 设置以提高性能
                client.NoDelay = true; // 禁用 Nagle 算法，减少延迟
                client.SendTimeout = 30000; // 增加超时时间以支持大文件（30秒）
                client.ReceiveTimeout = 5000; // 接收超时保持较短

                // 使用 CancellationToken 控制连接超时
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    await client.ConnectAsync(printerInfo.IpAddress, printerInfo.Port).WaitAsync(cts.Token);
                }

                _logger.LogInformation($"已连接到打印机: {printerInfo.IpAddress}:{printerInfo.Port}");

                networkStream = client.GetStream();
                
                // 设置网络流缓冲区大小和超时
                networkStream.WriteTimeout = 30000;
                networkStream.ReadTimeout = 5000;
                
                // 发送初始化命令
                var initCommand = new byte[] { 0x1B, 0x40 };
                await networkStream.WriteAsync(initCommand, 0, initCommand.Length);
                _logger.LogDebug("已发送打印机初始化命令");

                // 使用流式传输文件内容，避免一次性加载到内存
                fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, 
                    FileOptions.SequentialScan | FileOptions.Asynchronous);
                
                long totalBytesSent = 0;
                const int bufferSize = 65536; // 64KB 缓冲区
                var buffer = new byte[bufferSize];
                
                // 对于多份打印，重复发送文件内容
                for (int copy = 0; copy < request.Copies; copy++)
                {
                    fileStream.Position = 0; // 重置文件流位置
                    long copyBytesSent = 0;
                    
                    _logger.LogInformation($"开始发送第 {copy + 1}/{request.Copies} 份打印内容");
                    
                    int bytesRead;
                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, bufferSize)) > 0)
                    {
                        await networkStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesSent += bytesRead;
                        copyBytesSent += bytesRead;
                    }
                    
                    _logger.LogInformation($"第 {copy + 1} 份打印内容已发送: {copyBytesSent} 字节");
                }

                // 发送打印并进纸命令
                var finishCommand = new byte[] { 0x1B, 0x64, 0x0A };
                await networkStream.WriteAsync(finishCommand, 0, finishCommand.Length);
                
                // 只刷新缓冲区，不等待确认（提高响应速度）
                await networkStream.FlushAsync();
                
                // 数据已发送完成，立即返回（不等待打印机处理完成）
                _logger.LogInformation($"打印数据已发送到网络缓冲区: 总计 {totalBytesSent} 字节");
                
                _logger.LogInformation($"打印任务已成功发送: 总计 {totalBytesSent} 字节, 任务ID={jobId}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogError($"连接打印机超时: {printerInfo.IpAddress}:{printerInfo.Port}");
                throw new TimeoutException($"连接打印机超时: {printerInfo.Name}");
            }
            catch (SocketException ex)
            {
                _logger.LogError(ex, $"网络连接错误: {printerInfo.IpAddress}:{printerInfo.Port}");
                throw;
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"文件读取或网络传输错误: {filePath}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"发送打印任务时发生未预期的错误: {printerInfo.Name}");
                throw;
            }
            finally
            {
                // 确保资源正确释放
                try
                {
                    fileStream?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "关闭文件流时发生错误");
                }

                try
                {
                    networkStream?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "关闭网络流时发生错误");
                }

                try
                {
                    client?.Close();
                    client?.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "关闭TCP连接时发生错误");
                }
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