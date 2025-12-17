namespace RemotePrintService.Models
{
    /// <summary>
    /// 打印请求模型
    /// </summary>
    public class PrintRequest
    {
        /// <summary>
        /// 文件路径（本地路径或URL）
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 打印机名称（可选，为空则使用默认打印机）
        /// </summary>
        public string? PrinterName { get; set; }

        /// <summary>
        /// 打印份数
        /// </summary>
        public int Copies { get; set; } = 1;

        /// <summary>
        /// 是否双面打印
        /// </summary>
        public bool DoubleSided { get; set; } = false;

        /// <summary>
        /// 纸张尺寸（如：A4, Letter等）
        /// </summary>
        public string? PaperSize { get; set; }

        /// <summary>
        /// 打印方向（Portrait 或 Landscape）
        /// </summary>
        public string Orientation { get; set; } = "Portrait";
    }
}