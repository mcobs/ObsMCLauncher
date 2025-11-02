using System;
using System.Diagnostics;
using System.Runtime;
using System.Windows.Threading;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 图片缓存管理器
    /// 用于定期清理未使用的图片缓存和触发垃圾回收以降低内存占用
    /// </summary>
    public static class ImageCacheManager
    {
        private static DispatcherTimer? _cleanupTimer;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 启动自动清理计时器
        /// </summary>
        public static void StartAutoCleanup()
        {
            if (_cleanupTimer != null)
                return;

            _cleanupTimer = new DispatcherTimer
            {
                Interval = CleanupInterval
            };

            _cleanupTimer.Tick += (s, e) => CleanupCache();
            _cleanupTimer.Start();

            Debug.WriteLine("[ImageCache] 自动清理已启动，间隔: 5分钟");
        }

        /// <summary>
        /// 停止自动清理计时器
        /// </summary>
        public static void StopAutoCleanup()
        {
            if (_cleanupTimer != null)
            {
                _cleanupTimer.Stop();
                _cleanupTimer = null;
                Debug.WriteLine("[ImageCache] 自动清理已停止");
            }
        }

        /// <summary>
        /// 手动触发缓存清理
        /// </summary>
        public static void CleanupCache()
        {
            try
            {
                var beforeMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0;

                // 清理LOH（大对象堆）碎片
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

                // 触发完整的垃圾回收
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);

                var afterMemory = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
                var freed = beforeMemory - afterMemory;

                if (freed > 0)
                {
                    Debug.WriteLine($"[ImageCache] 内存清理完成: 释放 {freed:F2} MB (从 {beforeMemory:F2} MB 降至 {afterMemory:F2} MB)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageCache] 清理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前内存使用情况（MB）
        /// </summary>
        public static double GetCurrentMemoryUsage()
        {
            return GC.GetTotalMemory(false) / 1024.0 / 1024.0;
        }
    }
}

