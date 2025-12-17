namespace RemotePrintService.Models
{
    /// <summary>
    /// 打印机信息
    /// </summary>
    public class PrinterInfo
    {
        /// <summary>
        /// 打印机名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 打印机IP地址
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// 打印机端口
        /// </summary>
        public int Port { get; set; } = 9100;

        /// <summary>
        /// 是否为默认打印机
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// 打印机描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 是否在线
        /// </summary>
        public bool IsOnline { get; set; }
    }
}
