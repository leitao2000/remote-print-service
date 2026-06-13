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
        /// 检测是否以 Windows 服务模式运行
        /// </summary>
        private static bool IsRunningAsService()
        {
            // 方法1: 检查父进程是否是 services.exe
            try
            {
                if (TryGetParentProcessName(out var parentName) && string.Equals(parentName, "services", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }

            // 方法2: 检查 SESSIONNAME 环境变量
            // 服务进程运行在 Session 0，SESSIONNAME 为空或为 "Services"
            string? sessionName = Environment.GetEnvironmentVariable("SESSIONNAME");
            if (string.IsNullOrEmpty(sessionName) || sessionName.Equals("Services", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 方法3: 检查是否有控制台窗口
            try
            {
                if (!Environment.UserInteractive)
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 尝试获取父进程名称（已妥善释放 Process 资源）
        /// </summary>
        private static bool TryGetParentProcessName(out string? name)
        {
            name = null;
            try
            {
                int parentPid = 0;
                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "wmic";
                process.StartInfo.Arguments = $"process where ProcessId={Environment.ProcessId} get ParentProcessId";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length >= 2 && int.TryParse(lines[1].Trim(), out int pid))
                {
                    parentPid = pid;
                }

                if (parentPid > 0)
                {
                    using var parent = System.Diagnostics.Process.GetProcessById(parentPid);
                    name = parent.ProcessName;
                    return true;
                }
            }
            catch
            {
                // 忽略，按未获取处理
            }
            return false;
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

                // 检查配置是否立即打印
                var immediatePrint = _configuration.GetValue<bool>("PrintSettings:ImmediatePrint", false);

                if (immediatePrint)
                {
                    // 立即执行打印，不使用后台任务
                    try
                    {
                        _logger.LogInformation($"立即打印任务 {jobId} 开始执行: 文件={request.FilePath}, 打印机={printerName}");
                        
                        if (fileExtension == ".pdf")
                        {
                            await PrintPdfAsync(request.FilePath, printerName, request.Copies, request.PaperSize, request.Orientation);
                            _logger.LogInformation($"PDF打印任务 {jobId} 已成功发送到打印机 '{printerName}'");
                        }
                        else if (fileExtension == ".txt")
                        {
                            await PrintTextFileAsync(request.FilePath, printerName, request.Copies);
                            _logger.LogInformation($"文本打印任务 {jobId} 已成功发送到打印机 '{printerName}'");
                        }
                        else
                        {
                            _logger.LogWarning($"不支持的文件格式: {fileExtension}，尝试使用默认程序打印");
                            await PrintWithDefaultProgramAsync(request.FilePath, printerName);
                        }

                        // 打印完成后，标记文件可以删除
                        if (request.FilePath.StartsWith(Path.GetTempPath()))
                        {
                            PrintFileCleanupService.MarkFileReadyForCleanup(request.FilePath, delayMs: 3000, _logger);
                        }

                        _logger.LogInformation($"打印任务 {jobId} 已完成");

                        return new PrintResponse
                        {
                            Success = true,
                            Message = $"打印任务已完成: '{printerName}'",
                            JobId = jobId,
                            Timestamp = DateTime.UtcNow
                        };
                    }
                    catch (FileNotFoundException ex)
                    {
                        _logger.LogError(ex, $"打印任务 {jobId} 失败: 文件不存在 - {ex.FileName}");
                        return new PrintResponse
                        {
                            Success = false,
                            Message = $"文件不存在: {ex.FileName}",
                            JobId = jobId,
                            ErrorCode = "FILE_NOT_FOUND"
                        };
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogError(ex, $"打印任务 {jobId} 失败: 操作无效 - {ex.Message}");
                        return new PrintResponse
                        {
                            Success = false,
                            Message = $"操作无效: {ex.Message}",
                            JobId = jobId,
                            ErrorCode = "INVALID_OPERATION"
                        };
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogError(ex, $"打印任务 {jobId} 失败: 权限不足 - {ex.Message}");
                        return new PrintResponse
                        {
                            Success = false,
                            Message = $"权限不足: {ex.Message}",
                            JobId = jobId,
                            ErrorCode = "ACCESS_DENIED"
                        };
                    }
                    catch (Exception printEx)
                    {
                        _logger.LogError(printEx, $"打印任务 {jobId} 执行失败: 打印机={printerName}, 文件={request.FilePath}, 错误类型={printEx.GetType().Name}, 错误消息={printEx.Message}, 堆栈跟踪={printEx.StackTrace}");
                        return new PrintResponse
                        {
                            Success = false,
                            Message = $"打印失败: {printEx.Message}",
                            JobId = jobId,
                            ErrorCode = "PRINT_ERROR"
                        };
                    }
                }
                else
                {
                    // 6. 根据文件类型执行打印（优化为后台异步执行，立即返回响应）
                    // 注意：使用Task.Run确保异常不会被吞掉，并记录详细错误
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            _logger.LogInformation($"后台打印任务 {jobId} 开始执行: 文件={request.FilePath}, 打印机={printerName}");
                            
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
                        catch (FileNotFoundException ex)
                        {
                            _logger.LogError(ex, $"后台打印任务 {jobId} 失败: 文件不存在 - {ex.FileName}");
                        }
                        catch (InvalidOperationException ex)
                        {
                            _logger.LogError(ex, $"后台打印任务 {jobId} 失败: 操作无效 - {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.LogError(ex, $"后台打印任务 {jobId} 失败: 权限不足 - {ex.Message}");
                        }
                        catch (Exception printEx)
                        {
                            _logger.LogError(printEx, $"后台打印任务 {jobId} 执行失败: 打印机={printerName}, 文件={request.FilePath}, 错误类型={printEx.GetType().Name}, 错误消息={printEx.Message}, 堆栈跟踪={printEx.StackTrace}");
                        }
                    }).ContinueWith(task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            _logger.LogError(task.Exception, $"后台打印任务 {jobId} 发生未处理的异常");
                        }
                    }, TaskContinuationOptions.OnlyOnFaulted);

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
                // 使用 PowerShell 查询 Win32_PrintJob 获取打印队列任务数
                string escapedName = printerName.Replace("'", "''");
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"(Get-CimInstance -ClassName Win32_PrintJob | Where-Object {{ $_.PrinterName -eq '{escapedName}' }} | Measure-Object).Count\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        if (int.TryParse(output.Trim(), out int count))
                        {
                            return count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"获取打印队列计数时发生错误: {printerName}");
            }

            return 0;
        }

        /// <summary>
        /// 打印 PDF 文件
        /// 优先使用 SumatraPDF（支持 Windows 服务模式，无需 GUI）
        /// 备选使用 Adobe Acrobat（仅在桌面模式下可用）
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

                if (!string.IsNullOrEmpty(paperSize) || !string.IsNullOrEmpty(orientation))
                {
                    _logger.LogInformation($"请求的打印参数: 纸张={paperSize}, 方向={orientation}");
                }

                string absoluteFilePath = Path.GetFullPath(filePath);

                if (!System.IO.File.Exists(absoluteFilePath))
                    throw new FileNotFoundException($"文件不存在或已被删除: {absoluteFilePath}");

                // 验证文件可读
                using (var fs = System.IO.File.OpenRead(absoluteFilePath)) { }

                // 读取配置的打印方式：Auto / SumatraPDF / Acrobat
                string printMethod = (_configuration["PdfPrint:PrintMethod"] ?? "Auto").Trim();
                _logger.LogInformation($"配置的打印方式: {printMethod}");

                switch (printMethod.ToLower())
                {
                    case "sumatrapdf":
                        await PrintWithSumatraPdfConfigured(absoluteFilePath, printerName, copies);
                        break;
                    case "acrobat":
                        await PrintWithAcrobatConfigured(absoluteFilePath, printerName);
                        break;
                    default: // Auto
                        await PrintWithAutoMethod(absoluteFilePath, printerName, copies);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PDF 打印失败: {filePath}");
                throw;
            }
        }

        /// <summary>
        /// 配置指定使用 SumatraPDF 打印
        /// </summary>
        private async Task PrintWithSumatraPdfConfigured(string filePath, string printerName, int copies)
        {
            string sumatraPath = _configuration["PdfPrint:SumatraPdfPath"] ?? string.Empty;
            if (!System.IO.File.Exists(sumatraPath))
            {
                throw new InvalidOperationException(
                    $"配置指定使用 SumatraPDF，但未找到程序: {sumatraPath}。" +
                    "请下载 SumatraPDF 并配置 PdfPrint:SumatraPdfPath。" +
                    "下载地址: https://www.sumatrapdfreader.org/download-free-pdf-viewer");
            }
            await PrintWithSumatraPdfAsync(sumatraPath, filePath, printerName, copies);
        }

        /// <summary>
        /// 配置指定使用 Adobe Acrobat 打印
        /// </summary>
        private async Task PrintWithAcrobatConfigured(string filePath, string printerName)
        {
            string adobeAcrobatPath = FindAdobeAcrobat();
            await PrintWithAcrobatAsync(adobeAcrobatPath, filePath, printerName);
        }

        /// <summary>
        /// 自动选择打印方式（SumatraPDF 优先，Acrobat 备选）
        /// </summary>
        private async Task PrintWithAutoMethod(string filePath, string printerName, int copies)
        {
            // 优先 SumatraPDF（服务模式可用）
            string sumatraPath = _configuration["PdfPrint:SumatraPdfPath"] ?? string.Empty;
            if (System.IO.File.Exists(sumatraPath))
            {
                _logger.LogInformation($"自动选择 SumatraPDF 打印: {sumatraPath}");
                await PrintWithSumatraPdfAsync(sumatraPath, filePath, printerName, copies);
                return;
            }

            // 备选 Adobe Acrobat（仅桌面模式可用）
            if (IsRunningAsService())
            {
                throw new InvalidOperationException(
                    "当前运行在 Windows 服务模式下，但未找到 SumatraPDF。" +
                    "服务模式下无法使用 Adobe Acrobat 打印。" +
                    "请下载 SumatraPDF 并配置 PdfPrint:SumatraPdfPath，" +
                    "或将 PdfPrint:PrintMethod 改为 SumatraPDF 或 Acrobat。" +
                    "下载地址: https://www.sumatrapdfreader.org/download-free-pdf-viewer");
            }

            string adobeAcrobatPath = FindAdobeAcrobat();
            _logger.LogInformation($"自动选择 Adobe Acrobat 打印: {adobeAcrobatPath}");
            await PrintWithAcrobatAsync(adobeAcrobatPath, filePath, printerName);
        }

        /// <summary>
        /// 使用 SumatraPDF 打印（支持服务模式，无需 GUI）
        /// </summary>
        private async Task PrintWithSumatraPdfAsync(string sumatraPath, string filePath, string printerName, int copies)
        {
            for (int i = 0; i < copies; i++)
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = sumatraPath,
                    Arguments = $"-print-to \"{printerName}\" \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                _logger.LogInformation($"SumatraPDF 打印命令: \"{sumatraPath}\" -print-to \"{printerName}\" \"{filePath}\" (第 {i + 1}/{copies} 份)");

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                    throw new InvalidOperationException("无法启动 SumatraPDF 进程");

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    _logger.LogWarning($"SumatraPDF 进程退出码: {process.ExitCode}（第 {i + 1} 份）");
                else
                    _logger.LogInformation($"SumatraPDF 打印完成（第 {i + 1}/{copies} 份）");
            }

            PrintFileCleanupService.MarkFileReadyForCleanup(filePath, delayMs: 3000, _logger);
        }

        /// <summary>
        /// 使用 Adobe Acrobat 打印（仅桌面模式可用）
        /// </summary>
        private async Task PrintWithAcrobatAsync(string acrobatPath, string filePath, string printerName)
        {
            string? fileDirectory = Path.GetDirectoryName(filePath);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = acrobatPath,
                Arguments = $"/t \"{filePath}\" \"{printerName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = fileDirectory
            };

            _logger.LogInformation($"Acrobat 打印命令: \"{acrobatPath}\" /t \"{filePath}\" \"{printerName}\"");

            System.Diagnostics.Process? process = null;
            try
            {
                process = System.Diagnostics.Process.Start(startInfo);
            }
            catch (System.ComponentModel.Win32Exception win32Ex)
            {
                throw new InvalidOperationException($"无法启动 Adobe Acrobat: {win32Ex.Message}", win32Ex);
            }

            if (process == null)
                throw new InvalidOperationException("无法启动 Adobe Acrobat 进程");

            // 后台等待进程退出后清理文件
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync();
                    _logger.LogInformation($"Acrobat 打印进程已退出，退出码: {process.ExitCode}");
                    PrintFileCleanupService.MarkFileReadyForCleanup(filePath, delayMs: 3000, _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "等待 Acrobat 进程退出时发生错误");
                    PrintFileCleanupService.MarkFileReadyForCleanup(filePath, delayMs: 5000, _logger);
                }
                finally
                {
                    process.Dispose();
                }
            });

            await Task.Delay(1000); // 等待进程启动
        }

        /// <summary>
        /// 查找 Adobe Acrobat 安装路径
        /// </summary>
        private string FindAdobeAcrobat()
        {
            string configuredPath = _configuration["PdfPrint:AdobeAcrobatPath"]
                ?? @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";

            if (System.IO.File.Exists(configuredPath))
                return configuredPath;

            string[] fallbackPaths = {
                @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe",
                @"C:\Program Files (x86)\Adobe\Acrobat DC\Acrobat\Acrobat.exe",
                @"C:\Program Files\Adobe\Acrobat Reader DC\Acrobat\Acrobat.exe",
                @"C:\Program Files (x86)\Adobe\Acrobat Reader DC\Acrobat\Acrobat.exe",
            };

            foreach (var path in fallbackPaths)
            {
                if (System.IO.File.Exists(path))
                {
                    _logger.LogInformation($"找到 Adobe Acrobat: {path}");
                    return path;
                }
            }

            throw new InvalidOperationException(
                "未找到 Adobe Acrobat。请安装 Adobe Acrobat 或配置 PdfPrint:AdobeAcrobatPath。");
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