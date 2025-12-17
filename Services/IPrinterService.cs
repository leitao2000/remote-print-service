using RemotePrintService.Models;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 打印机服务接口
    /// </summary>
    public interface IPrinterService
    {
        /// <summary>
        /// 获取所有可用的打印机列表
        /// </summary>
        Task<List<PrinterInfo>> GetAvailablePrintersAsync();

        /// <summary>
        /// 获取指定打印机的信息
        /// </summary>
        Task<PrinterInfo?> GetPrinterInfoAsync(string printerName);

        /// <summary>
        /// 执行打印操作
        /// </summary>
        Task<PrintResponse> PrintAsync(PrintRequest request);

        /// <summary>
        /// 检查打印机是否在线
        /// </summary>
        Task<bool> IsPrinterOnlineAsync(string printerName);

        /// <summary>
        /// 获取打印队列中的任务数
        /// </summary>
        Task<int> GetPrintQueueCountAsync(string printerName);
    }
}
