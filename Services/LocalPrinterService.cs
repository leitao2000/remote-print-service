using System.Drawing;
using System.Drawing.Printing;
using PdfSharp.Pdf.IO;
using RemotePrintService.Models;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 本地打印机服务实现类
    /// </summary>
    public class LocalPrinterService : IPrinterService
    {
        private readonly ILogger<LocalPrinterService> _logger;
        private readonly IConfiguration _configuration;

        public LocalPrinterService(ILogger<LocalPrinterService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// 获取所有可用的本地打印机列表（带初始化预热，解决首次访问问题）
        /// </summary>
        public async Task<List<PrinterInfo>> GetAvailablePrintersAsync()
        {
            var printers = new List<PrinterInfo>();

            try
            {
                // 预热打印机系统（首次访问时可能需要初始化）
                try
                {
                    _ = PrinterSettings.InstalledPrinters.Count;
                    await Task.Delay(100); // 给系统一点初始化时间
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "预热打印机系统时发生错误，继续执行");
                }

                foreach (string printerName in PrinterSettings.InstalledPrinters)
                {
                    try
                    {
                        var settings = new PrinterSettings { PrinterName = printerName };
                        var isDefault = printerName == await GetDefaultPrinterName();
                        
                        // 使用更可靠的方法检查打印机状态
                        bool isValid = settings.IsValid;
                        bool isOnline = isValid && !settings.IsPlotter;
                        
                        var printer = new PrinterInfo
                        {
                            Name = printerName,
                            IpAddress = "localhost",
                            Port = 0,
                            IsDefault = isDefault,
                            Description = $"本地打印机 - {printerName}",
                            IsOnline = isOnline
                        };

                        printers.Add(printer);
                        _logger.LogInformation($"发现本地打印机: {printerName} (状态: {(isOnline ? "在线" : "离线")})");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"获取打印机信息时发生错误: {printerName}");
                        // 即使出错也添加打印机信息，但标记为离线
                        printers.Add(new PrinterInfo
                        {
                            Name = printerName,
                            IpAddress = "localhost",
                            Port = 0,
                            IsDefault = false,
                            Description = $"本地打印机 - {printerName}",
                            IsOnline = false
                        });
                    }
                }

                _logger.LogInformation($"共找到 {printers.Count} 个本地打印机");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取可用本地打印机时发生错误");
            }

            return await Task.FromResult(printers);
        }

        /// <summary>
        /// 获取指定打印机的详细信息
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
        /// 执行打印操作（带完善的错误校验和异常处理）
        /// </summary>
        public async Task<PrintResponse> PrintAsync(PrintRequest request)
        {
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

                // 3. 获取打印机名称
                string printerName = request.PrinterName ?? await GetDefaultPrinterName();

                if (string.IsNullOrEmpty(printerName))
                {
                    _logger.LogWarning("未找到默认打印机");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = "未找到默认打印机",
                        ErrorCode = "NO_PRINTER_AVAILABLE"
                    };
                }

                // 4. 获取打印机信息
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

                // 5. 检查打印机在线状态（带重试机制，解决首次访问初始化问题）
                bool isOnline = false;
                const int maxStatusChecks = 2;
                for (int i = 0; i < maxStatusChecks; i++)
                {
                    isOnline = await IsPrinterOnlineAsync(printerName);
                    if (isOnline) break;
                    
                    if (i < maxStatusChecks - 1)
                    {
                        _logger.LogDebug($"打印机状态检查失败（尝试 {i + 1}/{maxStatusChecks}），等待后重试...");
                        await Task.Delay(500); // 等待500ms后重试
                    }
                }
                
                if (!isOnline)
                {
                    _logger.LogWarning($"打印机离线或未就绪: {printerName}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机离线或未就绪: {printerName}，请检查打印机连接和状态",
                        ErrorCode = "PRINTER_OFFLINE"
                    };
                }

                string jobId = Guid.NewGuid().ToString("N");
                string fileExtension = Path.GetExtension(request.FilePath).ToLower();

                _logger.LogInformation($"开始打印: 文件={request.FilePath}, 打印机={printerName}, 份数={request.Copies}, 格式={fileExtension}");

                // 6. 根据文件类型执行打印（优化为后台异步执行，立即返回响应）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (fileExtension == ".pdf")
                        {
                            await PrintPdfAsync(request.FilePath, printerName, request.Copies, request.PaperSize, request.Orientation);
                            _logger.LogInformation($"后台PDF打印任务 {jobId} 已成功发送到打印机 '{printerName}'");
                        }
                        else if (fileExtension == ".txt")
                        {
                            await PrintTextFileAsync(request.FilePath, printerName, request.Copies);
                            _logger.LogInformation($"后台文本打印任务 {jobId} 已成功发送到打印机 '{printerName}'");
                        }
                        else
                        {
                            _logger.LogWarning($"不支持的文件格式: {fileExtension}，尝试使用默认程序打印");
                            await PrintWithDefaultProgramAsync(request.FilePath, printerName);
                        }
                    }
                    catch (Exception printEx)
                    {
                        _logger.LogError(printEx, $"后台打印任务 {jobId} 执行失败: {printerName}");
                    }
                });

                // 立即返回响应，不等待打印完成
                _logger.LogInformation($"打印任务 {jobId} 已提交到后台队列，立即返回响应");

                return new PrintResponse
                {
                    Success = true,
                    Message = $"打印任务已提交到 '{printerName}'，正在后台处理",
                    JobId = jobId,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印操作发生未预期的错误");
                return PrintExceptionHandler.HandleException(ex, "打印操作");
            }
        }

        /// <summary>
        /// 检查指定打印机是否在线（带重试机制，解决首次访问初始化问题）
        /// </summary>
        public async Task<bool> IsPrinterOnlineAsync(string printerName)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 300;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // 预热打印机系统（首次访问时可能需要初始化）
                    if (attempt == 1)
                    {
                        // 触发打印机系统初始化
                        _ = PrinterSettings.InstalledPrinters.Count;
                        await Task.Delay(100); // 给系统一点初始化时间
                    }
                    
                    var settings = new PrinterSettings { PrinterName = printerName };
                    
                    // 使用更可靠的方法检查打印机状态
                    // 尝试访问打印机设置来验证打印机是否可用
                    bool isValid = settings.IsValid;
                    bool isOnline = isValid && !settings.IsPlotter;
                    
                    // 如果打印机有效，尝试获取默认页面设置来进一步验证
                    if (isValid)
                    {
                        try
                        {
                            _ = settings.DefaultPageSettings; // 访问此属性会触发打印机状态检查
                        }
                        catch
                        {
                            // 如果访问失败，可能打印机未就绪
                            if (attempt < maxRetries)
                            {
                                _logger.LogDebug($"打印机 {printerName} 状态检查失败（尝试 {attempt}/{maxRetries}），等待后重试...");
                                await Task.Delay(retryDelayMs * attempt); // 递增延迟
                                continue;
                            }
                            isOnline = false;
                        }
                    }
                    
                    _logger.LogInformation($"打印机 {printerName} 状态: {(isOnline ? "在线" : "离线")} (尝试 {attempt}/{maxRetries})");
                    return isOnline;
                }
                catch (Exception ex)
                {
                    if (attempt < maxRetries)
                    {
                        _logger.LogDebug(ex, $"检查打印机状态时发生错误（尝试 {attempt}/{maxRetries}）: {printerName}，等待后重试...");
                        await Task.Delay(retryDelayMs * attempt); // 递增延迟
                    }
                    else
                    {
                        _logger.LogWarning(ex, $"检查打印机状态时发生错误: {printerName}");
                        return false;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// 获取指定打印机的打印队列中的任务数
        /// </summary>
        public async Task<int> GetPrintQueueCountAsync(string printerName)
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = $"logicaldisk get name",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        return 0;
                    }
                }
            }
            catch (Exception ex)
            {
                // 使用 Spectre.Console 显示警告信息
                AnsiConsole.MarkupLine($"[yellow]获取打印队列计数时发生错误[/]: {printerName} - {ex.Message}");
                _logger.LogWarning(ex, $"获取打印队列计数时发生错误: {printerName}");
            }

            return await Task.FromResult(0);
        }

        /// <summary>
        /// 打印 PDF 文件
        /// </summary>
        private async Task PrintPdfAsync(string filePath, string printerName, int copies, string? paperSize = null, string? orientation = null)
        {
            try
            {
                _logger.LogInformation($"开始打印 PDF 文件: {filePath} -> {printerName}, 份数: {copies}");

                if (!System.IO.File.Exists(filePath))
                    throw new FileNotFoundException($"文件不存在: {filePath}");

                if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("仅支持 PDF 文件");

                if (!PrinterSettings.InstalledPrinters.Contains(printerName))
                    throw new InvalidOperationException($"打印机不存在: {printerName}");

                string adobeAcrobatPath = _configuration["PdfPrint:AdobeAcrobatPath"] 
                    ?? @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";

                if (!System.IO.File.Exists(adobeAcrobatPath))
                {
                    _logger.LogWarning($"Adobe Acrobat 未找到: {adobeAcrobatPath}，尝试使用默认路径");
                    string[] commonPaths = {
                        @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe",
                        @"C:\Program Files (x86)\Adobe\Acrobat DC\Acrobat\Acrobat.exe",
                        @"C:\Program Files\Adobe\Acrobat Reader DC\Acrobat\Acrobat.exe",
                        @"C:\Program Files (x86)\Adobe\Acrobat Reader DC\Acrobat\Acrobat.exe"
                    };

                    foreach (string path in commonPaths)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            adobeAcrobatPath = path;
                            break;
                        }
                    }

                    if (!System.IO.File.Exists(adobeAcrobatPath))
                    {
                        throw new InvalidOperationException($"未找到 Adobe Acrobat: {adobeAcrobatPath}");
                    }
                }

                if (!string.IsNullOrEmpty(paperSize) || !string.IsNullOrEmpty(orientation))
                {
                    _logger.LogInformation($"请求的打印参数: 纸张={paperSize}, 方向={orientation}");
                    
                    if (!string.IsNullOrEmpty(orientation) && orientation.ToLower() == "landscape")
                    {
                        _logger.LogWarning("请求横向打印: 请确保打印机已设置为横向模式");
                    }
                }

                // 将文件路径转换为绝对路径，确保Adobe Acrobat能够找到文件
                string absoluteFilePath = Path.GetFullPath(filePath);
                
                // 验证绝对路径文件存在（在启动进程前再次检查）
                if (!System.IO.File.Exists(absoluteFilePath))
                {
                    _logger.LogError($"文件不存在（绝对路径）: {absoluteFilePath} (原始路径: {filePath})");
                    throw new FileNotFoundException($"文件不存在或已被删除: {absoluteFilePath}");
                }

                // 获取文件所在目录作为工作目录
                string? fileDirectory = Path.GetDirectoryName(absoluteFilePath);
                if (string.IsNullOrEmpty(fileDirectory))
                {
                    _logger.LogWarning($"无法获取文件目录，使用当前工作目录");
                    fileDirectory = Directory.GetCurrentDirectory();
                }

                // 验证文件可读
                try
                {
                    using (var fileStream = System.IO.File.OpenRead(absoluteFilePath))
                    {
                        // 文件可读，继续
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"文件无法读取: {absoluteFilePath}");
                    throw new IOException($"文件无法读取: {absoluteFilePath}", ex);
                }

                // 转义文件路径中的特殊字符，确保路径正确传递给Adobe Acrobat
                // 使用双引号包裹路径，并转义路径中的引号
                string escapedFilePath = absoluteFilePath.Replace("\"", "\\\"");
                string escapedPrinterName = printerName.Replace("\"", "\\\"");

                var printProcess = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = adobeAcrobatPath,
                    Arguments = $"/t \"{escapedFilePath}\" \"{escapedPrinterName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    WorkingDirectory = fileDirectory // 设置工作目录为文件所在目录
                };

                _logger.LogInformation($"执行 Adobe Acrobat 打印命令:");
                _logger.LogInformation($"  程序路径: {adobeAcrobatPath}");
                _logger.LogInformation($"  文件路径: {absoluteFilePath}");
                _logger.LogInformation($"  打印机: {printerName}");
                _logger.LogInformation($"  工作目录: {fileDirectory}");
                _logger.LogInformation($"  完整命令: {adobeAcrobatPath} /t \"{escapedFilePath}\" \"{escapedPrinterName}\"");

                var process = System.Diagnostics.Process.Start(printProcess);
                if (process != null)
                {
                    int processId = process.Id;
                    _logger.LogInformation($"Adobe Acrobat 打印进程已启动，PID: {processId}");
                    
                    // 等待进程启动完成（首次启动可能需要更多时间）
                    // 使用短时间等待确保进程已启动，但不会阻塞太久
                    try
                    {
                        // 等待进程启动（首次启动 Adobe Acrobat 可能需要更长时间来打开文件）
                        await Task.Delay(1000); // 增加到1秒，确保进程有足够时间打开文件
                        if (!process.HasExited)
                        {
                            _logger.LogDebug("Adobe Acrobat 打印进程已成功启动");
                        }
                        else
                        {
                            _logger.LogWarning($"Adobe Acrobat 打印进程已退出，退出代码: {process.ExitCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "等待Adobe Acrobat进程启动时发生错误，继续执行");
                    }
                    
                    // 等待进程退出后再删除文件（确保打印完成）
                    // 进程会在后台继续执行打印任务，完成后删除文件
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 确保进程对象仍然有效
                            if (process != null && !process.HasExited)
                            {
                                await process.WaitForExitAsync();
                                int exitCode = process.ExitCode;
                                _logger.LogInformation($"Adobe Acrobat 打印进程已退出，PID: {processId}, 退出代码: {exitCode}");
                                
                                if (exitCode != 0)
                                {
                                    _logger.LogWarning($"Adobe Acrobat 打印进程返回非零退出代码: {exitCode}");
                                }
                                
                                // 打印完成后，标记文件可以删除（使用绝对路径）
                                PrintFileCleanupService.MarkFileReadyForCleanup(absoluteFilePath, delayMs: 3000, _logger);
                            }
                            else if (process != null)
                            {
                                // 进程已经退出
                                int exitCode = process.ExitCode;
                                _logger.LogInformation($"Adobe Acrobat 打印进程已退出，PID: {processId}, 退出代码: {exitCode}");
                                
                                // 打印完成后，标记文件可以删除（使用绝对路径）
                                PrintFileCleanupService.MarkFileReadyForCleanup(absoluteFilePath, delayMs: 3000, _logger);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // 进程对象已释放或不再关联，这是正常的，因为进程可能已经退出
                            _logger.LogDebug($"Adobe Acrobat 打印进程 (PID: {processId}) 已退出或对象已释放");
                            
                            // 即使出现异常，也尝试标记文件可以删除（延迟更长时间以确保安全，使用绝对路径）
                            PrintFileCleanupService.MarkFileReadyForCleanup(absoluteFilePath, delayMs: 5000, _logger);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"等待Adobe Acrobat进程退出时发生错误 (PID: {processId})");
                            
                            // 即使出现异常，也尝试标记文件可以删除（延迟更长时间以确保安全，使用绝对路径）
                            PrintFileCleanupService.MarkFileReadyForCleanup(absoluteFilePath, delayMs: 5000, _logger);
                        }
                        finally
                        {
                            // 确保释放进程对象
                            try
                            {
                                process?.Dispose();
                            }
                            catch
                            {
                                // 忽略释放时的异常
                            }
                        }
                    });
                }
                else
                {
                    _logger.LogError($"无法启动 Adobe Acrobat 打印进程");
                    throw new InvalidOperationException("无法启动 Adobe Acrobat 打印进程");
                }

                _logger.LogInformation($"PDF 文件已成功发送到打印机 '{printerName}'");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PDF 打印失败: {filePath}");
                throw;
            }
        }

        /// <summary>
        /// 打印文本文件（带初始化预热，解决首次打印失败问题）
        /// </summary>
        private async Task PrintTextFileAsync(string filePath, string printerName, int copies)
        {
            const int maxRetries = 2;
            const int retryDelayMs = 300;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation($"开始打印文本文件: {filePath} -> {printerName}, 份数: {copies} (尝试 {attempt}/{maxRetries})");
                    
                    // 将文件路径转换为绝对路径
                    string absoluteFilePath = Path.GetFullPath(filePath);
                    
                    // 验证文件存在
                    if (!System.IO.File.Exists(absoluteFilePath))
                    {
                        throw new FileNotFoundException($"文件不存在: {absoluteFilePath}");
                    }
                    
                    // 预热打印机系统（首次访问时可能需要初始化）
                    if (attempt == 1)
                    {
                        try
                        {
                            var testSettings = new PrinterSettings { PrinterName = printerName };
                            _ = testSettings.IsValid; // 触发打印机初始化
                            await Task.Delay(100); // 给系统一点初始化时间
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "预热打印机系统时发生错误，继续执行");
                        }
                    }
                    
                    var content = await File.ReadAllTextAsync(absoluteFilePath);

                    using (var printDoc = new System.Drawing.Printing.PrintDocument())
                    {
                        printDoc.PrinterSettings.PrinterName = printerName;
                        printDoc.PrinterSettings.Copies = (short)copies;
                        
                        // 验证打印机设置是否有效
                        if (!printDoc.PrinterSettings.IsValid)
                        {
                            throw new InvalidOperationException($"打印机设置无效: {printerName}");
                        }

                        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        int currentLine = 0;

                        printDoc.PrintPage += (sender, e) =>
                        {
                            var font = new System.Drawing.Font("Courier New", 10);
                            float yPosition = 20;
                            const float lineHeight = 15;
                            const float leftMargin = 20;

                            while (currentLine < lines.Length && yPosition < e.MarginBounds.Height)
                            {
                                e.Graphics?.DrawString(lines[currentLine], font, System.Drawing.Brushes.Black, leftMargin, yPosition);
                                yPosition += lineHeight;
                                currentLine++;
                            }

                            e.HasMorePages = currentLine < lines.Length;
                            // 减少过于频繁的日志输出
                            if (currentLine % 50 == 0 || currentLine >= lines.Length) // 每50行或最后一页输出一次
                            {
                                _logger.LogInformation($"打印页面，当前行: {currentLine}/{lines.Length}");
                            }
                        };

                        try
                        {
                            printDoc.Print();
                            _logger.LogInformation($"文本文件打印完成: {absoluteFilePath}");
                            return; // 成功，退出重试循环
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"PrintDocument.Print() 执行失败（尝试 {attempt}/{maxRetries}）");
                            
                            if (attempt < maxRetries)
                            {
                                await Task.Delay(retryDelayMs * attempt);
                                continue; // 重试
                            }
                            else
                            {
                                // 最后一次尝试，使用备用方法
                                _logger.LogInformation("尝试使用命令行方式打印");
                                await PrintUsingNotepadAsync(absoluteFilePath, printerName, copies);
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (attempt < maxRetries)
                    {
                        _logger.LogWarning(ex, $"打印文本文件失败（尝试 {attempt}/{maxRetries}），等待后重试...");
                        await Task.Delay(retryDelayMs * attempt);
                    }
                    else
                    {
                        _logger.LogError(ex, "打印文本文件时发生错误");
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 使用 Notepad 打印文本文件
        /// </summary>
        private async Task PrintUsingNotepadAsync(string filePath, string printerName, int copies)
        {
            try
            {
                // 将文件路径转换为绝对路径
                string absoluteFilePath = Path.GetFullPath(filePath);
                
                // 验证文件存在
                if (!System.IO.File.Exists(absoluteFilePath))
                {
                    throw new FileNotFoundException($"文件不存在: {absoluteFilePath}");
                }
                
                // 获取文件所在目录作为工作目录
                string? fileDirectory = Path.GetDirectoryName(absoluteFilePath);
                if (string.IsNullOrEmpty(fileDirectory))
                {
                    fileDirectory = Directory.GetCurrentDirectory();
                }
                
                // 转义文件路径中的特殊字符
                string escapedFilePath = absoluteFilePath.Replace("\"", "\\\"");
                
                _logger.LogInformation($"使用 Notepad 打印: {absoluteFilePath} -> {printerName}");
                
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"/p \"{escapedFilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = fileDirectory // 设置工作目录
                };

                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                        _logger.LogInformation($"Notepad 打印命令完成");
                    }
                }

                // 减少延迟时间
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notepad 打印失败");
                throw;
            }
        }

        /// <summary>
        /// 使用系统默认方法打印
        /// </summary>
        private Task PrintUsingWindowsDefaultAsync(string filePath, string printerName, int copies)
        {
            try
            {
                _logger.LogInformation($"使用 Windows 默认打印方法: {filePath}");
                
                throw new InvalidOperationException(
                    $"打印文件失败: {filePath}\n\n" +
                    $"请确保:\n" +
                    $"1. 文件格式受系统支持\n" +
                    $"2. 对于 PDF 文件: 安装 Adobe Reader 或配置默认 PDF 查看器\n" +
                    $"3. 打印机 '{printerName}' 已正确安装\n" +
                    $"4. 尝试右键点击文件并选择 '打印'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "使用 Windows 默认打印方法时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 使用默认程序打印
        /// </summary>
        private Task PrintWithDefaultProgramAsync(string filePath, string printerName)
        {
            try
            {
                return PrintUsingWindowsDefaultAsync(filePath, printerName, 1);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]使用默认程序打印时发生错误[/]: {ex.Message}");
                _logger.LogError(ex, "使用默认程序打印时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 获取默认打印机名称（带重试机制，解决首次访问初始化问题）
        /// </summary>
        private async Task<string> GetDefaultPrinterName()
        {
            const int maxRetries = 3;
            const int retryDelayMs = 200;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // 预热打印机系统（首次访问时可能需要初始化）
                    if (attempt == 1)
                    {
                        // 触发打印机系统初始化
                        _ = PrinterSettings.InstalledPrinters.Count;
                        await Task.Delay(100); // 给系统一点初始化时间
                    }
                    
                    var settings = new PrinterSettings();
                    string printerName = settings.PrinterName ?? 
                        (PrinterSettings.InstalledPrinters.Count > 0 ? PrinterSettings.InstalledPrinters[0] : string.Empty);
                    
                    if (!string.IsNullOrEmpty(printerName))
                    {
                        return printerName;
                    }
                    
                    // 如果没有默认打印机，尝试获取第一个可用打印机
                    if (PrinterSettings.InstalledPrinters.Count > 0)
                    {
                        return PrinterSettings.InstalledPrinters[0];
                    }
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs * attempt);
                    }
                }
                catch (Exception ex)
                {
                    if (attempt < maxRetries)
                    {
                        _logger.LogDebug(ex, $"获取默认打印机名称失败（尝试 {attempt}/{maxRetries}），等待后重试...");
                        await Task.Delay(retryDelayMs * attempt);
                    }
                    else
                    {
                        _logger.LogWarning(ex, "获取默认打印机名称时发生错误");
                    }
                }
            }
            
            // 最后一次尝试，不抛出异常
            try
            {
                return PrinterSettings.InstalledPrinters.Count > 0 ? PrinterSettings.InstalledPrinters[0] : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 检查文件是否被锁定
        /// </summary>
        private bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
                return false;
            }
            catch (IOException)
            {
                // 文件被锁定
                return true;
            }
            catch (Exception)
            {
                // 其他异常，假设文件被锁定以确保安全
                return true;
            }
        }
    }
}