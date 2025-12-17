using Microsoft.AspNetCore.Mvc;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using Spectre.Console;
using System.IO;

namespace RemotePrintService.Controllers
{
    /// <summary>
    /// 标签模板控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "v1")]
    [Tags("标签模板")]
    public class LabelController : ControllerBase
    {
        private readonly ILogger<LabelController> _logger;

        public LabelController(ILogger<LabelController> logger)
        {
            _logger = logger;
        }



        /// <summary>
        /// 根据数字生成标签PDF文件
        /// </summary>
        /// <param name="number">要绘制在标签上的数字</param>
        /// <returns>生成的PDF文件</returns>
        [HttpPost("generate-label")]
        [EndpointDescription("根据数字生成标签PDF文件")]
        [Consumes("application/json")]
        public IActionResult GenerateLabel([FromBody] LabelRequest request)
        {
            try
            {
                if (request.Number <= 0)
                {
                    return BadRequest(new { success = false, message = "数字必须大于0" });
                }

                // 创建PDF文档
                var document = new PdfDocument();
                var page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                page.Orientation = PdfSharp.PageOrientation.Portrait;

                var gfx = XGraphics.FromPdfPage(page);
                var font = new XFont("Arial", 20);

                // 绘制标签内容
                string labelText = request.Number.ToString();
                var textSize = gfx.MeasureString(labelText, font);
                
                // 居中绘制数字
                double x = (page.Width - textSize.Width) / 2;
                double y = (page.Height - textSize.Height) / 2;
                
                gfx.DrawString(labelText, font, XBrushes.Black, x, y);

                // 添加边框
                var pen = new XPen(XColors.Black, 2);
                gfx.DrawRectangle(pen, 50, 50, page.Width - 100, page.Height - 100);

                // 保存到内存流
                var stream = new MemoryStream();
                document.Save(stream, false);
                document.Close();

                // 重置流位置
                stream.Position = 0;

                _logger.LogInformation($"成功生成标签PDF，数字：{request.Number}");
                
                // 返回PDF文件
                return File(stream.ToArray(), "application/pdf", $"label_{request.Number}.pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成标签PDF时发生错误");
                return StatusCode(500, new { success = false, message = $"生成标签失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 根据数字生成标签PDF并直接打印
        /// </summary>
        /// <param name="number">要绘制在标签上的数字</param>
        /// <param name="printerName">打印机名称（可选）</param>
        /// <returns>打印结果</returns>
        [HttpPost("print-label")]
        [EndpointDescription("根据数字生成标签PDF并直接打印")]
        [Consumes("application/json")]
        public IActionResult GenerateAndPrintLabel([FromBody] PrintLabelRequest request)
        {
            try
            {
                if (request.Number <= 0)
                {
                    return BadRequest(new { success = false, message = "数字必须大于0" });
                }

                // 创建PDF文档
                var document = new PdfDocument();
                var page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                page.Orientation = PdfSharp.PageOrientation.Portrait;

                var gfx = XGraphics.FromPdfPage(page);
                var font = new XFont("Arial", 20);

                // 绘制标签内容
                string labelText = request.Number.ToString();
                var textSize = gfx.MeasureString(labelText, font);
                
                // 居中绘制数字
                double x = (page.Width - textSize.Width) / 2;
                double y = (page.Height - textSize.Height) / 2;
                
                gfx.DrawString(labelText, font, XBrushes.Black, x, y);

                // 添加边框
                var pen = new XPen(XColors.Black, 2);
                gfx.DrawRectangle(pen, 50, 50, page.Width - 100, page.Height - 100);

                // 保存到临时文件
                string tempPath = Path.Combine(Path.GetTempPath(), $"label_{Guid.NewGuid()}.pdf");
                document.Save(tempPath);
                document.Close();

                _logger.LogInformation($"成功生成标签PDF文件：{tempPath}，数字：{request.Number}");

                // 返回文件路径和基本信息
                return Ok(new 
                { 
                    success = true, 
                    message = "标签PDF生成成功", 
                    filePath = tempPath,
                    number = request.Number
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成并打印标签时发生错误");
                return StatusCode(500, new { success = false, message = $"生成标签失败: {ex.Message}" });
            }
        }
    }

    /// <summary>
    /// 标签请求模型
    /// </summary>
    public class LabelRequest
    {
        /// <summary>
        /// 要绘制在标签上的数字
        /// </summary>
        public int Number { get; set; }
    }

    /// <summary>
    /// 打印标签请求模型
    /// </summary>
    public class PrintLabelRequest
    {
        /// <summary>
        /// 要绘制在标签上的数字
        /// </summary>
        public int Number { get; set; }

        /// <summary>
        /// 打印机名称（可选）
        /// </summary>
        public string? PrinterName { get; set; }
    }
}