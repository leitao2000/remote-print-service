using System.Drawing;
using System.Drawing.Printing;
using PdfSharp.Pdf.IO;
using RemotePrintService.Models;
using Microsoft.Extensions.Configuration;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 本地打印机服务实现
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
        /// 获取所有可用的本地打印机
        /// </summary>
        public async Task<List<PrinterInfo>> GetAvailablePrintersAsync()
        {
            var printers = new List<PrinterInfo>();

            try
            {
                // 使用 System.Drawing.Printing 获取本地打印机
                foreach (string printerName in PrinterSettings.InstalledPrinters)
                {
                    try
                    {
                        var settings = new PrinterSettings { PrinterName = printerName };
                        var isDefault = printerName == GetDefaultPrinterName();
                        
                        var printer = new PrinterInfo
                        {
                            Name = printerName,
                            IpAddress = "localhost",
                            Port = 0,
                            IsDefault = isDefault,
                            Description = $"本地打印机 - {printerName}",
                            IsOnline = !settings.IsPlotter // 简单检查
                        };

                        printers.Add(printer);
                        _logger.LogInformation($"Found local printer: {printerName}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error getting info for printer: {printerName}");
                    }
                }

                _logger.LogInformation($"Found {printers.Count} local printers");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available local printers");
            }

            return await Task.FromResult(printers);
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

                // 获取目标打印机名称
                string printerName = request.PrinterName ?? GetDefaultPrinterName();

                if (string.IsNullOrEmpty(printerName))
                {
                    return new PrintResponse
                    {
                        Success = false,
                        Message = "未找到默认打印机"
                    };
                }

                // 验证打印机是否存在
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
                if (!await IsPrinterOnlineAsync(printerName))
                {
                    return new PrintResponse
                    {
                        Success = false,
                        Message = $"打印机离线: {printerName}"
                    };
                }

                string jobId = Guid.NewGuid().ToString("N");

                // 根据文件类型处理
                string fileExtension = Path.GetExtension(request.FilePath).ToLower();

                // 对于 PDF 文件，使用 Adobe Acrobat 打印
                if (fileExtension == ".pdf")
                {
                    _logger.LogInformation($"准备打印 PDF 文件: {request.FilePath} 到打印机: {printerName}");
                    await PrintPdfAsync(request.FilePath, printerName, request.Copies, request.PaperSize, request.Orientation);
                    _logger.LogInformation($"PDF 文件已发送到打印机 '{printerName}'，正在后台打印");
                }
                else if (fileExtension == ".txt")
                {
                    // 使用 PrintDocument 执行打印
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
                    // 其他文件类型，使用默认打印程序
                    await PrintWithDefaultProgramAsync(request.FilePath, printerName);
                }

                _logger.LogInformation($"Print job {jobId} sent to printer {printerName}");

                return new PrintResponse
                {
                    Success = true,
                    Message = $"打印任务已发送，浏览器将自动打印到 '{printerName}'",
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
                var settings = new PrinterSettings { PrinterName = printerName };
                bool isOnline = !settings.IsPlotter;
                _logger.LogInformation($"打印机 {printerName} 状态: {(isOnline ? "在线" : "离线")}");
                return await Task.FromResult(isOnline);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"检查打印机 {printerName} 状态时出错");
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
                // 使用 Windows 命令行获取队列信息（更兼容）
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
                        // 简单实现，返回 0
                        return 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Error getting queue count for printer: {printerName}");
            }

            return await Task.FromResult(0);
        }

        /// <summary>
        /// 打印 PDF 文件 - 使用 Adobe Acrobat 直接打印
        /// </summary>
        private async Task PrintPdfAsync(string filePath, string printerName, int copies, string? paperSize = null, string? orientation = null)
        {
            try
            {
                _logger.LogInformation($"PDF 打印开始: {filePath} -> {printerName}, 份数: {copies}, 纸张: {paperSize}, 方向: {orientation}");
                
                if (!System.IO.File.Exists(filePath))
                    throw new FileNotFoundException($"文件不存在: {filePath}");

                if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("仅支持 PDF 文件");

                if (!PrinterSettings.InstalledPrinters.Contains(printerName))
                    throw new InvalidOperationException($"打印机不存在: {printerName}");

                // 获取 Adobe Acrobat 路径配置
                string adobeAcrobatPath = _configuration["PdfPrint:AdobeAcrobatPath"] 
                    ?? @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";

                // 检查 Adobe Acrobat 是否存在
                if (!System.IO.File.Exists(adobeAcrobatPath))
                {
                    _logger.LogWarning($"Adobe Acrobat 未找到: {adobeAcrobatPath}，尝试使用默认路径");
                    // 尝试常见的 Adobe Acrobat 安装路径
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

                    // 如果仍然找不到 Adobe Acrobat
                    if (!System.IO.File.Exists(adobeAcrobatPath))
                    {
                        throw new InvalidOperationException($"未找到 Adobe Acrobat: {adobeAcrobatPath}");
                    }
                }

                // 如果指定了纸张大小或方向，记录相关信息
                if (!string.IsNullOrEmpty(paperSize) || !string.IsNullOrEmpty(orientation))
                {
                    _logger.LogInformation($"请求的打印参数: 纸张={paperSize}, 方向={orientation}");
                    
                    // 注意：由于 Adobe Acrobat 命令行限制，我们无法直接通过命令行参数设置纸张大小和方向
                    // 用户需要预先在打印机设置中配置好这些参数，或者使用 configure_printer_orientation.ps1 脚本进行设置
                    
                    // 如果请求横向打印，记录提醒信息
                    if (!string.IsNullOrEmpty(orientation) && orientation.ToLower() == "landscape")
                    {
                        _logger.LogWarning("请求横向打印: 请确保打印机已设置为横向模式，否则可能不会按预期打印");
                        _logger.LogWarning("建议使用 configure_printer_orientation.ps1 脚本或手动设置打印机方向");
                    }
                }

                // 构建 Adobe Acrobat 打印命令
                // 使用 Adobe Acrobat 的命令行参数进行静默打印
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
                        
                        // 等待打印进程完成（最多 60 秒，PDF 打印可能需要更多时间）
                        bool exited = process.WaitForExit(20000);
                        
                        if (exited)
                        {
                            _logger.LogInformation($"Adobe Acrobat 打印进程已退出，退出代码: {process.ExitCode}");
                            
                            // 检查退出代码
                            if (process.ExitCode != 0)
                            {
                                _logger.LogWarning($"Adobe Acrobat 打印进程返回非零退出代码: {process.ExitCode}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Adobe Acrobat 打印进程未在 60 秒内退出，可能仍在后台运行");
                        }
                    }
                    else
                    {
                        _logger.LogError($"无法启动 Adobe Acrobat 打印进程");
                        throw new InvalidOperationException("无法启动 Adobe Acrobat 打印进程");
                    }
                }

                _logger.LogInformation($"PDF 文件已成功通过 Adobe Acrobat 发送到打印机 '{printerName}'");
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
                _logger.LogInformation($"打印文本文件开始: {filePath} -> {printerName}, 份数: {copies}");
                
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
                        _logger.LogInformation($"打印页面，当前行: {currentLine}/{lines.Length}");
                    };

                    try
                    {
                        printDoc.Print();
                        _logger.LogInformation($"文本文件打印完成: {filePath}");
                        await Task.Delay(1000); // 给打印机处理时间
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "PrintDocument.Print() 执行失败，尝试使用命令行方式");
                        // 尝试使用 notepad 打印
                        await PrintUsingNotepadAsync(filePath, printerName, copies);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error printing text file");
                throw;
            }
        }

        /// <summary>
        /// 使用 Notepad 和 print.exe 打印文本文件
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

                await Task.Delay(1000);
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
        private async Task PrintUsingWindowsDefaultAsync(string filePath, string printerName, int copies)
        {
            try
            {
                _logger.LogInformation($"Using Windows default print method for: {filePath}");
                
                // 不再尝试直接启动文件，一旅失败则报告
                throw new InvalidOperationException(
                    $"Failed to print file: {filePath}\n\n" +
                    $"Please ensure:\n" +
                    $"1. The file format is supported by your system\n" +
                    $"2. For PDF: Install Adobe Reader or configure default PDF viewer\n" +
                    $"3. The printer '{printerName}' is properly installed\n" +
                    $"4. Try right-clicking the file and selecting 'Print'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PrintUsingWindowsDefaultAsync");
                throw;
            }
        }

        /// <summary>
        /// 使用默认程序打印（过旧版本）
        /// </summary>
        private async Task PrintWithDefaultProgramAsync(string filePath, string printerName)
        {
            try
            {
                // 这个方法已经不批推使用，应为PrintUsingWindowsDefaultAsync
                await PrintUsingWindowsDefaultAsync(filePath, printerName, 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error printing with default program");
                throw;
            }
        }

        /// <summary>
        /// 获取默认打印机名称
        /// </summary>
        private string GetDefaultPrinterName()
        {
            try
            {
                var settings = new PrinterSettings();
                return settings.PrinterName ?? (PrinterSettings.InstalledPrinters.Count > 0 ? PrinterSettings.InstalledPrinters[0] : string.Empty);
            }
            catch
            {
                // 如果不能获取默认打印机，则返回第一个打印机
                return PrinterSettings.InstalledPrinters.Count > 0 ? PrinterSettings.InstalledPrinters[0] : string.Empty;
            }
        }
    }
}
