namespace RemotePrintService.Models
{
    /// <summary>
    /// 打印机信息模型
    /// 用于封装打印机的基本信息和状态
    /// 包含打印机名称、网络地址、端口、在线状态等属性
    /// </summary>
    public class PrinterInfo
    {
        /// <summary>
        /// 打印机名称
        /// 打印机的唯一标识名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 打印机IP地址
        /// 打印机的网络IP地址
        /// 对于本地打印机，该值通常为"localhost"
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// 打印机端口
        /// 打印机监听的网络端口号
        /// 常见的网络打印机端口为9100
        /// 对于本地打印机，该值通常为0
        /// </summary>
        public int Port { get; set; } = 9100;

        /// <summary>
        /// 是否为默认打印机
        /// 标识该打印机是否被设置为系统默认打印机
        /// true: 是默认打印机
        /// false: 不是默认打印机
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// 打印机描述
        /// 对打印机的详细描述信息
        /// 可包含打印机型号、位置等附加信息
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 是否在线
        /// 表示打印机当前的在线状态
        /// true: 打印机在线可用
        /// false: 打印机离线不可用
        /// </summary>
        public bool IsOnline { get; set; }
    }
}