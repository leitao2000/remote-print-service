using RemotePrintService.Models;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 打印机服务接口
    /// 定义了打印机操作的标准接口，支持本地打印机和网络打印机的统一操作
    /// 实现类需要提供获取打印机列表、执行打印任务、检查打印机状态等功能
    /// </summary>
    public interface IPrinterService
    {
        /// <summary>
        /// 获取所有可用的打印机列表
        /// 返回系统中所有已安装或配置的打印机信息
        /// </summary>
        /// <returns>包含打印机信息的列表</returns>
        Task<List<PrinterInfo>> GetAvailablePrintersAsync();

        /// <summary>
        /// 获取指定打印机的详细信息
        /// 根据打印机名称查询其详细配置和状态信息
        /// </summary>
        /// <param name="printerName">要查询的打印机名称</param>
        /// <returns>指定打印机的详细信息，如果未找到则返回null</returns>
        Task<PrinterInfo?> GetPrinterInfoAsync(string printerName);

        /// <summary>
        /// 执行打印操作
        /// 根据提供的打印请求参数执行打印任务
        /// </summary>
        /// <param name="request">打印请求对象，包含文件路径、打印机名称、打印参数等</param>
        /// <returns>打印操作结果</returns>
        Task<PrintResponse> PrintAsync(PrintRequest request);

        /// <summary>
        /// 检查指定打印机是否在线
        /// 通过连接测试确定打印机的在线状态
        /// </summary>
        /// <param name="printerName">要检查的打印机名称</param>
        /// <returns>打印机是否在线的状态</returns>
        Task<bool> IsPrinterOnlineAsync(string printerName);

        /// <summary>
        /// 获取指定打印机的打印队列中的任务数
        /// 查询指定打印机当前排队等待打印的任务数量
        /// </summary>
        /// <param name="printerName">要查询的打印机名称</param>
        /// <returns>打印队列中的任务数</returns>
        Task<int> GetPrintQueueCountAsync(string printerName);
    }
}
