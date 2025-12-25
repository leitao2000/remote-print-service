using Microsoft.AspNetCore.Mvc;
using RemotePrintService.Models;
using RemotePrintService.Services;
using Spectre.Console;

namespace RemotePrintService.Controllers
{
    /// <summary>
    /// 打印机管理控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "v1")]
    [Tags("打印机管理")]
    public class PrinterController : ControllerBase
    {
        private readonly IPrinterService _printerService;
        private readonly ILogger<PrinterController> _logger;

        public PrinterController(IPrinterService printerService, ILogger<PrinterController> logger)
        {
            _printerService = printerService;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有可用的打印机列表
        /// </summary>
        [HttpGet("list")]
        [EndpointDescription("获取所有可用的打印机列表")]
        public async Task<IActionResult> GetAvailablePrinters()
        {
            try
            {
                var printers = await _printerService.GetAvailablePrintersAsync();
                return Ok(new { success = true, data = printers, message = "获取打印机列表成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取可用打印机列表时发生错误");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 获取指定打印机的详细信息
        /// </summary>
        [HttpGet("{printerName}")]
        [EndpointDescription("根据打印机名称获取详细信息")]
        public async Task<IActionResult> GetPrinterInfo(string printerName)
        {
            try
            {
                var printer = await _printerService.GetPrinterInfoAsync(printerName);
                
                if (printer == null)
                {
                    _logger.LogWarning($"打印机不存在: {printerName}");
                    return NotFound(new { success = false, message = $"打印机不存在: {printerName}" });
                }

                _logger.LogInformation($"成功获取打印机信息: {printer.Name}");
                return Ok(new { success = true, data = printer });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取打印机信息时发生错误: {printerName}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 检查打印机是否在线
        /// </summary>
        [HttpGet("{printerName}/status")]
        [EndpointDescription("检查指定打印机是否在线")]
        public async Task<IActionResult> CheckPrinterStatus(string printerName)
        {
            try
            {
                var isOnline = await _printerService.IsPrinterOnlineAsync(printerName);
                
                // 使用 Spectre.Console 显示状态信息
                var statusText = isOnline ? "[green]在线[/]" : "[red]离线[/]";
                AnsiConsole.MarkupLine($"打印机 [blue]{printerName}[/] 状态: {statusText}");
                
                return Ok(new 
                { 
                    success = true, 
                    data = new { printerName, isOnline }, 
                    message = isOnline ? "打印机在线" : "打印机离线" 
                });
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]检查打印机状态时发生错误: {printerName} - {ex.Message}[/]");
                _logger.LogError(ex, $"检查打印机状态时发生错误: {printerName}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 获取打印队列中的任务数
        /// </summary>
        [HttpGet("{printerName}/queue-count")]
        [EndpointDescription("获取打印机队列中的任务数")]
        public async Task<IActionResult> GetQueueCount(string printerName)
        {
            try
            {
                var count = await _printerService.GetPrintQueueCountAsync(printerName);
                
                _logger.LogInformation($"打印机 {printerName} 队列任务数: {count}");
                
                return Ok(new { success = true, data = new { printerName, queueCount = count } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取打印队列计数时发生错误: {printerName}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 执行打印操作（带完善的错误处理）
        /// </summary>
        [HttpPost("print")]
        [EndpointDescription("向指定打印机发送打印任务")]
        [Consumes("application/json")]
        public async Task<IActionResult> Print([FromBody] PrintRequest? request)
        {
            try
            {
                if (request == null)
                {
                    _logger.LogWarning("打印请求体为NULL");
                    return BadRequest(new PrintResponse
                    {
                        Success = false,
                        Message = "请求体不能为空，请提供有效的JSON请求体",
                        ErrorCode = "INVALID_REQUEST"
                    });
                }

                _logger.LogInformation($"接收到打印请求：文件={request.FilePath}, 打印机={request.PrinterName ?? "默认"}, 份数={request.Copies}");

                // 调用服务层执行打印（服务层已包含完整的验证和错误处理）
                var result = await _printerService.PrintAsync(request);

                if (result.Success)
                {
                    _logger.LogInformation($"打印任务提交成功：任务ID={result.JobId}, 重试次数={result.RetryCount}");
                    return Ok(result);
                }
                else
                {
                    // 根据错误代码返回适当的HTTP状态码
                    var statusCode = result.ErrorCode switch
                    {
                        "VALIDATION_ERROR" or "INVALID_PRINTER_NAME" or "INVALID_NETWORK_ADDRESS" => 400, // Bad Request
                        "FILE_NOT_FOUND" or "PRINTER_NOT_FOUND" or "NO_PRINTER_AVAILABLE" => 404, // Not Found
                        "PRINTER_OFFLINE" or "NETWORK_ERROR" or "TIMEOUT" => 503, // Service Unavailable
                        "ACCESS_DENIED" => 403, // Forbidden
                        _ => 500 // Internal Server Error
                    };

                    _logger.LogWarning($"打印任务提交失败：{result.Message}, 错误代码={result.ErrorCode}, HTTP状态码={statusCode}");
                    return StatusCode(statusCode, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印操作发生未预期的异常");
                var errorResponse = PrintExceptionHandler.HandleException(ex, "打印操作");
                return StatusCode(500, errorResponse);
            }
        }

        /// <summary>
        /// 上传文件并直接打印
        /// </summary>
        [HttpPost("print-file")]
        [EndpointDescription("上传文件并直接打印")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> PrintFile(
            [FromForm] string? printerName,
            [FromForm] int? copies,
            [FromForm] bool? doubleSided,
            [FromForm] string? paperSize)
        {
            try
            {
                var file = Request.Form.Files.FirstOrDefault();
                
                if (file == null)
                {
                    _logger.LogWarning("未提供文件，请上传一个有效的文件");
                    return BadRequest(new { success = false, message = "未提供文件，请上传一个有效的文件" });
                }

                if (file.Length == 0)
                {
                    _logger.LogWarning("上传的文件为空，上传大小大于 0 的文件");
                    return BadRequest(new { success = false, message = "上传的文件为空，上传大小大于 0 的文件" });
                }

                _logger.LogInformation($"接收到文件：{file.FileName}，大小：{file.Length} 字节");

                // 保存临时文件
                string tempPath = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}");
                
                using (var stream = System.IO.File.Create(tempPath))
                {
                    await file.CopyToAsync(stream);
                }

                _logger.LogInformation($"临时文件已保存：{tempPath}");

                // 设置默认值
                int finalCopies = copies ?? 1;
                bool finalDoubleSided = doubleSided ?? false;

                var printRequest = new PrintRequest
                {
                    FilePath = tempPath,
                    PrinterName = printerName,
                    Copies = finalCopies,
                    DoubleSided = finalDoubleSided,
                    PaperSize = paperSize
                };

                var result = await _printerService.PrintAsync(printRequest);

                // 注册文件到清理服务，在打印完成后自动删除
                // 这样确保 Adobe Acrobat 有足够时间打开和打印文件
                if (result.Success && !string.IsNullOrEmpty(result.JobId))
                {
                    PrintFileCleanupService.RegisterFileForCleanup(tempPath, result.JobId, _logger);
                    _logger.LogDebug($"已注册临时文件等待打印完成后清理: {tempPath} (任务ID: {result.JobId})");
                }
                else
                {
                    // 如果打印失败，立即尝试清理文件
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000); // 等待2秒后尝试删除
                        PrintFileCleanupService.CleanupFileNow(tempPath);
                    });
                }

                if (result.Success)
                {
                    _logger.LogInformation($"文件打印任务提交成功：任务ID={result.JobId}");
                    return Ok(result);
                }
                else
                {
                    _logger.LogWarning($"文件打印任务提交失败：{result.Message}");
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印操作失败");
                return StatusCode(500, new { success = false, message = $"打印失败: {ex.Message}" });
            }
        }
    }
}