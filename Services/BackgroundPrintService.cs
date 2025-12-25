using RemotePrintService.Models;
using System.Collections.Concurrent;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 后台打印任务服务
    /// 用于异步执行打印任务，提高接口响应速度
    /// </summary>
    public class BackgroundPrintService
    {
        private readonly ConcurrentDictionary<string, PrintJobStatus> _jobStatuses = new();
        private readonly ILogger<BackgroundPrintService> _logger;
        private readonly IPrinterService _printerService;

        public BackgroundPrintService(ILogger<BackgroundPrintService> logger, IPrinterService printerService)
        {
            _logger = logger;
            _printerService = printerService;
        }

        /// <summary>
        /// 提交打印任务到后台执行
        /// </summary>
        /// <param name="request">打印请求</param>
        /// <param name="jobId">任务ID</param>
        /// <returns>任务ID</returns>
        public string SubmitPrintJob(PrintRequest request, string jobId)
        {
            // 初始化任务状态
            _jobStatuses.TryAdd(jobId, new PrintJobStatus
            {
                JobId = jobId,
                Status = "Submitted",
                SubmittedAt = DateTime.UtcNow,
                Request = request
            });

            // 在后台执行打印任务（不等待完成）
            _ = Task.Run(async () =>
            {
                try
                {
                    UpdateJobStatus(jobId, "Processing", "正在处理打印任务");
                    _logger.LogInformation($"后台打印任务开始: {jobId}");

                    var result = await _printerService.PrintAsync(request);

                    if (result.Success)
                    {
                        UpdateJobStatus(jobId, "Completed", result.Message, result);
                        _logger.LogInformation($"后台打印任务完成: {jobId}");
                    }
                    else
                    {
                        UpdateJobStatus(jobId, "Failed", result.Message, result);
                        _logger.LogWarning($"后台打印任务失败: {jobId}, 错误: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    UpdateJobStatus(jobId, "Failed", $"打印任务执行异常: {ex.Message}");
                    _logger.LogError(ex, $"后台打印任务异常: {jobId}");
                }
            });

            return jobId;
        }

        /// <summary>
        /// 获取任务状态
        /// </summary>
        public PrintJobStatus? GetJobStatus(string jobId)
        {
            _jobStatuses.TryGetValue(jobId, out var status);
            return status;
        }

        /// <summary>
        /// 更新任务状态
        /// </summary>
        private void UpdateJobStatus(string jobId, string status, string message, PrintResponse? result = null)
        {
            if (_jobStatuses.TryGetValue(jobId, out var jobStatus))
            {
                jobStatus.Status = status;
                jobStatus.Message = message;
                jobStatus.UpdatedAt = DateTime.UtcNow;
                if (result != null)
                {
                    jobStatus.Result = result;
                }
            }
        }

        /// <summary>
        /// 清理旧的任务状态（可选，用于内存管理）
        /// </summary>
        public void CleanupOldJobs(TimeSpan maxAge)
        {
            var cutoffTime = DateTime.UtcNow - maxAge;
            var keysToRemove = _jobStatuses
                .Where(kvp => kvp.Value.UpdatedAt < cutoffTime && 
                             (kvp.Value.Status == "Completed" || kvp.Value.Status == "Failed"))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _jobStatuses.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation($"清理了 {keysToRemove.Count} 个旧的打印任务状态");
            }
        }
    }

    /// <summary>
    /// 打印任务状态
    /// </summary>
    public class PrintJobStatus
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Submitted, Processing, Completed, Failed
        public string Message { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public PrintRequest? Request { get; set; }
        public PrintResponse? Result { get; set; }
    }
}

