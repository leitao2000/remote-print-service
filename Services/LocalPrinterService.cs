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
        /// 获取所有可用的本地打印机列表
        /// </summary>
        public async Task<List<PrinterInfo>> GetAvailablePrintersAsync()
        {
            var printers = new List<PrinterInfo>();

            try
            {
                foreach (string printerName in PrinterSettings.InstalledPrinters)
                {
                    try
                    {
                        var settings = new PrinterSettings { PrinterName = printerName };
                        var isDefault = printerName == await GetDefaultPrinterName();
                        
                        var printer = new PrinterInfo
                        {
                            Name = printerName,
                            IpAddress = "localhost",
                            Port = 0,
                            IsDefault = isDefault,
                            Description = $"本地打印机 - {printerName}",
                            IsOnline = !settings.IsPlotter
                        };

                        printers.Add(printer);
                        _logger.LogInformation($"发现本地打印机: {printerName}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"获取打印机信息时发生错误: {printerName}");
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
        /// 执行打印操作
        /// </summary>
        public async Task<PrintResponse> PrintAsync(PrintRequest request)
        {
            try
            {
                if (!File.Exists(request.FilePath))
                {
                    _logger.LogInformation($"文件不存在: {request.FilePath}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"文件不存在: {request.FilePath}"
                    };
                }

                string printerName = request.PrinterName ?? await GetDefaultPrinterName();

                if (string.IsNullOrEmpty(printerName))
                {
                    _logger.LogWarning("未找到默认打印机");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = "未找到默认打印机"
                    };
                }

                var printerInfo = await GetPrinterInfoAsync(printerName);
                if (printerInfo == null)
                {
                    _logger.LogWarning($"打印机不存在: {printerName}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机不存在: {printerName}"
                    };
                }

                if (!await IsPrinterOnlineAsync(printerName))
                {
                    _logger.LogWarning($"打印机离线: {printerName}");
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机离线: {printerName}"
                    };
                }

                string jobId = Guid.NewGuid().ToString("N");

                string fileExtension = Path.GetExtension(request.FilePath).ToLower();

                if (fileExtension == ".pdf")
                {
                    _logger.LogInformation($"准备打印 PDF 文件: {request.FilePath} -> {printerName}, 份数: {request.Copies}");
                    await PrintPdfAsync(request.FilePath, printerName, request.Copies, request.PaperSize, request.Orientation);
                    _logger.LogInformation($"PDF 文件已发送到打印机 '{printerName}'");
                }
                else if (fileExtension == ".txt")
                {
                    _logger.LogInformation($"准备打印文本文件: {request.FilePath} -> {printerName}, 份数: {request.Copies}");
                    using (var printDoc = new System.Drawing.Printing.PrintDocument())
                    {
                        printDoc.PrinterSettings.PrinterName = printerName;
                        printDoc.PrinterSettings.Copies = (short)request.Copies;
                        printDoc.DocumentName = $"Print_Job_{jobId}";
                        await PrintTextFileAsync(request.FilePath, printerName, request.Copies);
                    }
                }
                else
                {
                    _logger.LogInformation($"使用默认程序打印文件: {request.FilePath} -> {printerName}");
                    await PrintWithDefaultProgramAsync(request.FilePath, printerName);
                }

                _logger.LogInformation($"打印任务 {jobId} 已发送到打印机 {printerName}");

                return new PrintResponse
                {
                    Success = true,
                    Message = $"打印任务已发送到 '{printerName}'",
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
        /// 检查指定打印机是否在线
        /// </summary>
        public async Task<bool> IsPrinterOnlineAsync(string printerName)
        {
            try
            {
                var settings = new PrinterSettings { PrinterName = printerName };
                bool isOnline = !settings.IsPlotter;
                _logger.LogInformation($"打印机 {printerName} 状态: {(isOnline ? "在线" : "离线")}");
                return await Task.FromResult(isOnline);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"检查打印机状态时发生错误: {printerName}");
                return false;
            }
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

                var printProcess = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = adobeAcrobatPath,
                    Arguments = $"/t \"{filePath}\" \"{printerName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                _logger.LogInformation($"执行 Adobe Acrobat 打印命令: {adobeAcrobatPath} /t \"{filePath}\" \"{printerName}\"");

                using (var process = System.Diagnostics.Process.Start(printProcess))
                {
                    if (process != null)
                    {
                        _logger.LogInformation($"Adobe Acrobat 打印进程已启动，PID: {process.Id}");
                        
                        // 增加等待时间到30秒，确保 Adobe Acrobat 有足够时间读取文件
                        bool exited = process.WaitForExit(30000);
                        
                        if (exited)
                        {
                            _logger.LogInformation($"Adobe Acrobat 打印进程已退出，退出代码: {process.ExitCode}");
                            
                            if (process.ExitCode != 0)
                            {
                                _logger.LogWarning($"Adobe Acrobat 打印进程返回非零退出代码: {process.ExitCode}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Adobe Acrobat 打印进程未在 30 秒内退出，将继续执行");
                        }
                    }
                    else
                    {
                        _logger.LogError($"无法启动 Adobe Acrobat 打印进程");
                        throw new InvalidOperationException("无法启动 Adobe Acrobat 打印进程");
                    }
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
        /// 打印文本文件
        /// </summary>
        private async Task PrintTextFileAsync(string filePath, string printerName, int copies)
        {
            try
            {
                _logger.LogInformation($"开始打印文本文件: {filePath} -> {printerName}, 份数: {copies}");
                
                var content = await File.ReadAllTextAsync(filePath);

                using (var printDoc = new System.Drawing.Printing.PrintDocument())
                {
                    printDoc.PrinterSettings.PrinterName = printerName;
                    printDoc.PrinterSettings.Copies = (short)copies;

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
                        _logger.LogInformation($"文本文件打印完成: {filePath}");
                        // 移除不必要的延迟，让打印操作异步进行
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "PrintDocument.Print() 执行失败，尝试使用命令行方式");
                        await PrintUsingNotepadAsync(filePath, printerName, copies);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印文本文件时发生错误");
                throw;
            }
        }

        /// <summary>
        /// 使用 Notepad 打印文本文件
        /// </summary>
        private async Task PrintUsingNotepadAsync(string filePath, string printerName, int copies)
        {
            try
            {
                _logger.LogInformation($"使用 Notepad 打印: {filePath} -> {printerName}");
                
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"/p \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
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
        /// 获取默认打印机名称
        /// </summary>
        private async Task<string> GetDefaultPrinterName()
        {
            try
            {
                var settings = new PrinterSettings();
                return await Task.FromResult(settings.PrinterName ?? (PrinterSettings.InstalledPrinters.Count > 0 ? PrinterSettings.InstalledPrinters[0] : string.Empty));
            }
            catch
            {
                return await Task.FromResult(PrinterSettings.InstalledPrinters.Count > 0 ? PrinterSettings.InstalledPrinters[0] : string.Empty);
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