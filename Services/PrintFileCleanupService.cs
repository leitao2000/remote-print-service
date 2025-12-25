using System.Collections.Concurrent;

namespace RemotePrintService.Services
{
    /// <summary>
    /// 打印文件清理服务 - 在打印完成后删除临时文件
    /// </summary>
    public class PrintFileCleanupService
    {
        private static readonly ConcurrentDictionary<string, FileCleanupInfo> _pendingFiles = new();
        private static readonly ILogger<PrintFileCleanupService>? _logger;

        /// <summary>
        /// 注册需要清理的文件（在打印完成后删除）
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="jobId">打印任务ID（可选）</param>
        /// <param name="logger">日志记录器（可选）</param>
        public static void RegisterFileForCleanup(string filePath, string? jobId = null, ILogger? logger = null)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                return;
            }

            var info = new FileCleanupInfo
            {
                FilePath = filePath,
                JobId = jobId,
                RegisteredAt = DateTime.UtcNow,
                Logger = logger
            };

            _pendingFiles.TryAdd(filePath, info);
            logger?.LogDebug($"已注册文件等待清理: {filePath} (任务ID: {jobId})");
        }

        /// <summary>
        /// 标记文件可以删除（打印已完成）
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="delayMs">延迟删除时间（毫秒），默认1000ms</param>
        /// <param name="logger">日志记录器（可选）</param>
        public static void MarkFileReadyForCleanup(string filePath, int delayMs = 1000, ILogger? logger = null)
        {
            if (_pendingFiles.TryGetValue(filePath, out var info))
            {
                info.IsReadyForCleanup = true;
                info.ReadyAt = DateTime.UtcNow;
                // 如果提供了 logger，更新 info 中的 logger
                if (logger != null)
                {
                    info.Logger = logger;
                }

                // 延迟删除，确保文件完全释放
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs);
                        await CleanupFileAsync(filePath, info);
                    }
                    catch (Exception ex)
                    {
                        info.Logger?.LogWarning(ex, $"清理文件时发生错误: {filePath}");
                    }
                });
            }
            else
            {
                // 如果文件未注册，直接尝试删除（可能是直接调用打印的情况）
                logger?.LogDebug($"文件未注册到清理服务，将直接删除: {filePath}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs);
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath);
                            logger?.LogInformation($"临时文件已删除: {filePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, $"删除临时文件失败: {filePath}");
                    }
                });
            }
        }

        /// <summary>
        /// 立即清理文件（如果已注册）
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public static void CleanupFileNow(string filePath)
        {
            if (_pendingFiles.TryRemove(filePath, out var info))
            {
                _ = Task.Run(async () => await CleanupFileAsync(filePath, info));
            }
        }

        /// <summary>
        /// 清理文件
        /// </summary>
        private static async Task CleanupFileAsync(string filePath, FileCleanupInfo info)
        {
            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    info.Logger?.LogDebug($"文件已不存在，无需清理: {filePath}");
                    _pendingFiles.TryRemove(filePath, out _);
                    return;
                }

                // 检查文件是否仍被占用，最多等待30秒
                int attempts = 0;
                const int maxAttempts = 60; // 60次，每次500ms，总共30秒
                bool fileIsLocked = true;

                while (attempts < maxAttempts && fileIsLocked)
                {
                    try
                    {
                        // 尝试以独占方式打开文件以检查是否被锁定
                        using (var stream = System.IO.File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                        {
                            stream.Close();
                            fileIsLocked = false;
                            info.Logger?.LogDebug($"文件已解锁，可以删除: {filePath}");
                            break;
                        }
                    }
                    catch (FileNotFoundException)
                    {
                        // 文件已被删除
                        info.Logger?.LogDebug($"文件已被删除: {filePath}");
                        fileIsLocked = false;
                        _pendingFiles.TryRemove(filePath, out _);
                        return;
                    }
                    catch (IOException)
                    {
                        // 文件仍被锁定，等待后重试
                        attempts++;
                        if (attempts < maxAttempts)
                        {
                            await Task.Delay(500);
                        }
                        else
                        {
                            info.Logger?.LogWarning($"文件在30秒后仍被锁定，将稍后重试删除: {filePath}");
                            // 如果30秒后仍被锁定，再等待一段时间后重试
                            await Task.Delay(10000); // 再等待10秒
                            fileIsLocked = false; // 强制尝试删除
                        }
                    }
                }

                // 尝试删除文件
                if (System.IO.File.Exists(filePath))
                {
                    try
                    {
                        System.IO.File.Delete(filePath);
                        info.Logger?.LogInformation($"临时文件已删除: {filePath} (任务ID: {info.JobId})");
                    }
                    catch (IOException ex)
                    {
                        // 文件仍被占用，记录警告但不抛出异常
                        info.Logger?.LogWarning(ex, $"删除临时文件失败（文件仍被占用）: {filePath}，将在应用程序退出时自动清理");
                    }
                    catch (Exception ex)
                    {
                        info.Logger?.LogWarning(ex, $"删除临时文件失败: {filePath}，将在应用程序退出时自动清理");
                    }
                }

                _pendingFiles.TryRemove(filePath, out _);
            }
            catch (Exception ex)
            {
                info.Logger?.LogWarning(ex, $"清理文件时发生错误: {filePath}");
            }
        }

        /// <summary>
        /// 文件清理信息
        /// </summary>
        private class FileCleanupInfo
        {
            public string FilePath { get; set; } = string.Empty;
            public string? JobId { get; set; }
            public DateTime RegisteredAt { get; set; }
            public DateTime? ReadyAt { get; set; }
            public bool IsReadyForCleanup { get; set; }
            public ILogger? Logger { get; set; }
        }
    }
}

