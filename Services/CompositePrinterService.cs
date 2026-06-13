using RemotePrintService.Models;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 组合打印机服务 - 同时支持本地打印机和网络打印机
    /// </summary>
    public class CompositePrinterService : IPrinterService
    {
        private readonly LocalPrinterService _localService;
        private readonly NetworkPrinterService _networkService;
        private readonly ILogger<CompositePrinterService> _logger;

        public CompositePrinterService(
            LocalPrinterService localService,
            NetworkPrinterService networkService,
            ILogger<CompositePrinterService> logger)
        {
            _localService = localService;
            _networkService = networkService;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有可用打印机（本地 + 网络）
        /// </summary>
        public async Task<List<PrinterInfo>> GetAvailablePrintersAsync()
        {
            var printers = new List<PrinterInfo>();

            var localTask = _localService.GetAvailablePrintersAsync();
            var networkTask = _networkService.GetAvailablePrintersAsync();

            await Task.WhenAll(localTask, networkTask);

            printers.AddRange(await localTask);
            printers.AddRange(await networkTask);

            _logger.LogInformation($"共找到 {printers.Count} 个打印机（本地 + 网络）");
            return printers;
        }

        /// <summary>
        /// 获取指定打印机信息（先查本地，再查网络）
        /// </summary>
        public async Task<PrinterInfo?> GetPrinterInfoAsync(string printerName)
        {
            var localPrinter = await _localService.GetPrinterInfoAsync(printerName);
            if (localPrinter != null) return localPrinter;

            return await _networkService.GetPrinterInfoAsync(printerName);
        }

        /// <summary>
        /// 执行打印操作（根据打印机类型自动路由）
        /// </summary>
        public async Task<PrintResponse> PrintAsync(PrintRequest request)
        {
            var printerName = request.PrinterName ?? string.Empty;

            // 先检查是否为本地打印机
            var localPrinter = await _localService.GetPrinterInfoAsync(printerName);
            if (localPrinter != null)
            {
                _logger.LogInformation($"路由到本地打印机: {printerName}");
                return await _localService.PrintAsync(request);
            }

            // 再检查是否为网络打印机
            var networkPrinter = await _networkService.GetPrinterInfoAsync(printerName);
            if (networkPrinter != null)
            {
                _logger.LogInformation($"路由到网络打印机: {printerName}");
                return await _networkService.PrintAsync(request);
            }

            // 都找不到，尝试用本地服务（会使用默认打印机）
            _logger.LogWarning($"未找到指定打印机 '{printerName}'，尝试使用本地默认打印机");
            return await _localService.PrintAsync(request);
        }

        /// <summary>
        /// 检查打印机是否在线（先查本地，再查网络）
        /// </summary>
        public async Task<bool> IsPrinterOnlineAsync(string printerName)
        {
            var localPrinter = await _localService.GetPrinterInfoAsync(printerName);
            if (localPrinter != null)
                return await _localService.IsPrinterOnlineAsync(printerName);

            return await _networkService.IsPrinterOnlineAsync(printerName);
        }

        /// <summary>
        /// 获取打印队列任务数（先查本地，再查网络）
        /// </summary>
        public async Task<int> GetPrintQueueCountAsync(string printerName)
        {
            var localPrinter = await _localService.GetPrinterInfoAsync(printerName);
            if (localPrinter != null)
                return await _localService.GetPrintQueueCountAsync(printerName);

            return await _networkService.GetPrintQueueCountAsync(printerName);
        }
    }
}
