using Microsoft.AspNetCore.Mvc;
using RemotePrintService.Models;
using RemotePrintService.Services;

namespace RemotePrintService.Controllers
{
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
        /// 获取所有可用的打印机
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
                _logger.LogError(ex, "Error getting available printers");
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
                    return NotFound(new { success = false, message = $"打印机不存在: {printerName}" });
                }

                return Ok(new { success = true, data = printer });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting printer info: {printerName}");
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
                return Ok(new 
                { 
                    success = true, 
                    data = new { printerName, isOnline }, 
                    message = isOnline ? "打印机在线" : "打印机离线" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking printer status: {printerName}");
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
                return Ok(new { success = true, data = new { printerName, queueCount = count } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting queue count: {printerName}");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// 执行打印操作
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
                    return BadRequest(new { success = false, message = "请求体为NULL，请提供JSON操作体" });
                }

                _logger.LogInformation($"接收到打印请求：文件={request.FilePath}, 打印机={request.PrinterName}");

                if (string.IsNullOrWhiteSpace(request.FilePath))
                {
                    return BadRequest(new { success = false, message = "文件路径不能为空" });
                }

                if (request.Copies < 1 || request.Copies > 100)
                {
                    return BadRequest(new { success = false, message = "打印份数必须在1-100之间" });
                }

                var result = await _printerService.PrintAsync(request);

                if (result.Success)
                {
                    _logger.LogInformation($"打印成功：{result.JobId}");
                    return Ok(result);
                }
                else
                {
                    _logger.LogWarning($"打印失败：{result.Message}");
                    return BadRequest(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "打印操作失败");
                return StatusCode(500, new { success = false, message = $"打印失败: {ex.Message}" });
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
                // 从表单中获取第一个文件
                var file = Request.Form.Files.FirstOrDefault();
                
                _logger.LogInformation($"收到请求，文件数量: {Request.Form.Files.Count}");
                foreach (var f in Request.Form.Files)
                {
                    _logger.LogInformation($"文件: {f.FileName}, 大小: {f.Length}");
                }
                
                if (file == null)
                {
                    return BadRequest(new { success = false, message = "未提供文件，请上传一个有效的文件" });
                }

                if (file.Length == 0)
                {
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

                // 打印完成后再删除临时文件（确保 Adobe Acrobat 已经读取完文件）
                // 延迟更长时间，确保 Adobe 完全处理完文件
                if (System.IO.File.Exists(tempPath))
                {
                    try
                    {
                        // 延迟 3 秒，确保 Adobe Acrobat 完全读取并处理完文件
                        await Task.Delay(3000);
                        
                        // 尝试删除文件，如果文件仍被占用则稍后重试
                        int retryCount = 0;
                        while (retryCount < 5 && System.IO.File.Exists(tempPath))
                        {
                            try
                            {
                                System.IO.File.Delete(tempPath);
                                _logger.LogInformation($"临时文件已删除：{tempPath}");
                                break;
                            }
                            catch (IOException)
                            {
                                // 文件可能仍在使用中，等待后重试
                                retryCount++;
                                if (retryCount < 5)
                                {
                                    await Task.Delay(1000);
                                }
                            }
                        }
                        
                        if (System.IO.File.Exists(tempPath))
                        {
                            _logger.LogWarning($"临时文件删除失败，可能仍在使用中：{tempPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"删除临时文件失败：{tempPath}，可能文件仍在使用中");
                        // 不抛出异常，打印可能已经成功
                    }
                }

                if (result.Success)
                {
                    return Ok(result);
                }
                else
                {
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
