namespace RemotePrintService.Models
{
    /// <summary>
    /// 打印请求模型
    /// 用于封装客户端发起的打印请求参数
    /// 包含文件路径、打印机名称、打印份数等打印相关配置
    /// </summary>
    public class PrintRequest
    {
        /// <summary>
        /// 文件路径（本地路径或URL）
        /// 指定要打印的文件位置，可以是本地文件系统路径或网络URL
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// 打印机名称（可选，为空则使用默认打印机）
        /// 指定目标打印机的名称，如果不提供则使用系统默认打印机
        /// </summary>
        public string? PrinterName { get; set; }

        /// <summary>
        /// 打印份数
        /// 指定需要打印的副本数量，默认为1份
        /// 有效范围通常为1-100份
        /// </summary>
        public int Copies { get; set; } = 1;

        /// <summary>
        /// 是否双面打印
        /// 指定是否启用双面打印功能，默认为单面打印
        /// </summary>
        public bool DoubleSided { get; set; } = false;

        /// <summary>
        /// 纸张尺寸（如：A4, Letter等）
        /// 指定打印时使用的纸张尺寸规格
        /// </summary>
        public string? PaperSize { get; set; }

        /// <summary>
        /// 打印方向（Portrait 或 Landscape）
        /// 指定打印页面的方向，默认为纵向(Portrait)
        /// Portrait: 纵向打印
        /// Landscape: 横向打印
        /// </summary>
        public string Orientation { get; set; } = "Portrait";
    }
}