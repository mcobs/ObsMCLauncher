using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 系统信息工具类
    /// </summary>
    public static class SystemInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        /// <summary>
        /// 获取系统总内存（MB）
        /// </summary>
        public static long GetTotalMemoryMB()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Windows: 使用GlobalMemoryStatusEx
                    var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                    if (GlobalMemoryStatusEx(ref memStatus))
                    {
                        return (long)(memStatus.ullTotalPhys / (1024 * 1024));
                    }
                }

                // 备用方法：使用GC
                var totalMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (totalMemoryBytes > 0)
                {
                    return totalMemoryBytes / (1024 * 1024);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取系统总内存失败: {ex.Message}");
            }

            // 默认返回16GB
            return 16384;
        }

        /// <summary>
        /// 获取系统可用内存（MB）
        /// </summary>
        public static long GetAvailableMemoryMB()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Windows: 使用GlobalMemoryStatusEx
                    var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                    if (GlobalMemoryStatusEx(ref memStatus))
                    {
                        return (long)(memStatus.ullAvailPhys / (1024 * 1024));
                    }
                }

                // 备用方法：使用PerformanceCounter
                using var counter = new PerformanceCounter("Memory", "Available MBytes");
                return (long)counter.NextValue();
            }
            catch
            {
                // 如果失败，假设一半内存可用
                return GetTotalMemoryMB() / 2;
            }
        }

        /// <summary>
        /// 获取当前进程使用的内存（MB）
        /// </summary>
        public static long GetProcessMemoryMB()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 格式化内存大小显示
        /// </summary>
        public static string FormatMemorySize(long memoryMB)
        {
            if (memoryMB >= 1024)
            {
                return $"{memoryMB / 1024.0:F1} GB";
            }
            return $"{memoryMB} MB";
        }

        /// <summary>
        /// 获取系统信息摘要
        /// </summary>
        public static string GetSystemInfoSummary()
        {
            try
            {
                var totalMem = GetTotalMemoryMB();
                var availableMem = GetAvailableMemoryMB();
                var processMem = GetProcessMemoryMB();
                var usedMem = totalMem - availableMem;

                return $"系统内存: {FormatMemorySize(usedMem)} / {FormatMemorySize(totalMem)} | 启动器: {FormatMemorySize(processMem)}";
            }
            catch
            {
                return "系统信息获取失败";
            }
        }

        /// <summary>
        /// 获取推荐的最大内存分配（系统总内存的75%）
        /// </summary>
        public static int GetRecommendedMaxMemoryMB()
        {
            var totalMem = GetTotalMemoryMB();
            return (int)(totalMem * 0.75);
        }
    }
}


