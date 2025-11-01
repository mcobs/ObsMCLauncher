using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Forge 和 OptiFine 兼容性判断服务
    /// 负责判断特定版本的 Forge 和 OptiFine 是否可以一起安装
    /// </summary>
    public static class ForgeOptiFineCompatibilityService
    {
        /// <summary>
        /// 判断指定的 Minecraft 版本、Forge 版本和 OptiFine 版本是否兼容
        /// </summary>
        /// <param name="mcVersion">Minecraft 版本（如 1.20.1）</param>
        /// <param name="forgeVersion">Forge 版本（如 47.2.0）</param>
        /// <param name="optifineVersion">OptiFine 版本信息</param>
        /// <returns>兼容性结果</returns>
        public static CompatibilityResult CheckCompatibility(
            string mcVersion, 
            string forgeVersion, 
            OptifineVersionModel optifineVersion)
        {
            try
            {
                Debug.WriteLine($"[Compatibility] 检查兼容性: MC={mcVersion}, Forge={forgeVersion}, OptiFine={optifineVersion.FullVersion}");

                // 1. 检查 Minecraft 版本
                var versionParts = ParseVersion(mcVersion);
                if (versionParts == null)
                {
                    return new CompatibilityResult
                    {
                        IsCompatible = false,
                        Reason = "无法解析 Minecraft 版本号"
                    };
                }

                int major = versionParts.Value.Major;
                int minor = versionParts.Value.Minor;
                int patch = versionParts.Value.Patch;

                // 2. 检查是否是已知不兼容的版本范围
                
                // 2.1 Minecraft 1.17+ 需要 OptiFine H1 Pre2 或更高版本
                if (major == 1 && minor >= 17)
                {
                    if (!IsOptiFineCompatibleWith117(optifineVersion))
                    {
                        return new CompatibilityResult
                        {
                            IsCompatible = false,
                            Reason = $"Minecraft {mcVersion} 需要 OptiFine H1 Pre2 或更高版本才能与 Forge 一起使用"
                        };
                    }
                }

                // 2.2 检查 Forge 版本是否在已知不兼容范围内（48.0.0 - 49.0.50）
                if (IsForgeInBrokenRange(forgeVersion))
                {
                    return new CompatibilityResult
                    {
                        IsCompatible = false,
                        Reason = $"Forge 版本 {forgeVersion} 与 OptiFine 存在已知兼容性问题，建议使用其他版本"
                    };
                }

                // 2.3 Minecraft 1.13 以下版本，Forge 和 OptiFine 可以完全集成安装
                // 1.13+ 版本，OptiFine 应作为 mod 安装
                if (major == 1 && minor < 13)
                {
                    return new CompatibilityResult
                    {
                        IsCompatible = true,
                        InstallMode = InstallMode.Integrated,
                        Reason = $"Minecraft {mcVersion} 支持 Forge 和 OptiFine 集成安装"
                    };
                }

                // 2.4 检查 OptiFine 是否在元数据中声明了兼容的 Forge 版本
                if (!string.IsNullOrEmpty(optifineVersion.Forge))
                {
                    Debug.WriteLine($"[Compatibility] OptiFine 声明兼容 Forge: {optifineVersion.Forge}");
                    
                    // 解析声明的 Forge 版本（格式如 "Forge 43.1.1"）
                    var match = Regex.Match(optifineVersion.Forge, @"Forge\s+([\d.]+)");
                    if (match.Success)
                    {
                        string declaredForgeVersion = match.Groups[1].Value;
                        
                        // 检查当前 Forge 版本是否匹配或接近
                        if (IsForgeVersionCompatible(forgeVersion, declaredForgeVersion))
                        {
                            return new CompatibilityResult
                            {
                                IsCompatible = true,
                                InstallMode = InstallMode.AsMod,
                                Reason = $"OptiFine {optifineVersion.FullVersion} 官方支持 Forge {declaredForgeVersion}，可作为 mod 安装"
                            };
                        }
                    }
                }

                // 3. 默认情况：1.13+ 版本可以将 OptiFine 作为 mod 安装
                if (major == 1 && minor >= 13)
                {
                    return new CompatibilityResult
                    {
                        IsCompatible = true,
                        InstallMode = InstallMode.AsMod,
                        Reason = $"Minecraft {mcVersion} 支持将 OptiFine 作为 Forge mod 安装",
                        WarningMessage = "OptiFine 与 Forge 的兼容性可能因版本而异，如遇问题请尝试其他版本"
                    };
                }

                // 4. 其他情况默认兼容，但给出警告
                return new CompatibilityResult
                {
                    IsCompatible = true,
                    InstallMode = InstallMode.AsMod,
                    Reason = "可以尝试将 OptiFine 作为 mod 安装",
                    WarningMessage = "此组合未经测试，可能存在兼容性问题"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Compatibility] 检查兼容性时出错: {ex.Message}");
                return new CompatibilityResult
                {
                    IsCompatible = false,
                    Reason = $"检查兼容性时出错: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 检查 OptiFine 是否与 Minecraft 1.17+ 兼容
        /// 需要 H1 Pre2 或更高版本
        /// </summary>
        private static bool IsOptiFineCompatibleWith117(OptifineVersionModel optifine)
        {
            // OptiFine 版本格式: HD_U_H9, HD_U_H1_pre2, etc.
            // 对于 1.17+，需要 H1 Pre2 或更高版本
            
            var fullVersion = optifine.FullVersion;
            
            // 提取版本号（如 H9, H1, I5）
            var match = Regex.Match(fullVersion, @"([A-Z])(\d+)(?:_pre(\d+))?", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                Debug.WriteLine($"[Compatibility] 无法解析 OptiFine 版本: {fullVersion}");
                return false; // 无法解析，保守起见返回不兼容
            }

            string letter = match.Groups[1].Value.ToUpper();
            int number = int.Parse(match.Groups[2].Value);
            bool isPre = match.Groups[3].Success;
            int preNumber = isPre ? int.Parse(match.Groups[3].Value) : int.MaxValue;

            // H1 Pre2 及以上兼容
            // 比较逻辑：
            // - 字母序：I > H (ASCII 码比较)
            // - 同字母比较数字：H2 > H1
            // - 同版本比较 pre：正式版 > pre 版本，pre2 >= pre2
            
            if (letter.CompareTo("H") > 0) // I, J, K... 都兼容
            {
                return true;
            }
            else if (letter == "H")
            {
                if (number > 1) // H2, H3... 都兼容
                {
                    return true;
                }
                else if (number == 1)
                {
                    // H1 版本：需要 Pre2 或正式版
                    if (!isPre) // H1 正式版
                    {
                        return true;
                    }
                    else if (preNumber >= 2) // H1 Pre2 或更高
                    {
                        return true;
                    }
                }
            }

            Debug.WriteLine($"[Compatibility] OptiFine {fullVersion} 不满足 1.17+ 兼容性要求（需要 H1 Pre2+）");
            return false;
        }

        /// <summary>
        /// 检查 Forge 版本是否在已知不兼容范围内（48.0.0 - 49.0.50）
        /// </summary>
        private static bool IsForgeInBrokenRange(string forgeVersion)
        {
            var version = ParseVersion(forgeVersion);
            if (version == null) return false;

            int major = version.Value.Major;
            int minor = version.Value.Minor;
            int patch = version.Value.Patch;

            // 检查是否在 48.0.0 - 49.0.50 范围内
            if (major == 48)
            {
                return true; // 48.x.x 都不兼容
            }
            else if (major == 49)
            {
                // 49.0.0 - 49.0.50 不兼容
                if (minor == 0 && patch <= 50)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查两个 Forge 版本是否兼容（版本号接近）
        /// </summary>
        private static bool IsForgeVersionCompatible(string version1, string version2)
        {
            var v1 = ParseVersion(version1);
            var v2 = ParseVersion(version2);

            if (v1 == null || v2 == null) return false;

            // 主版本号必须相同
            if (v1.Value.Major != v2.Value.Major) return false;

            // 次版本号差距不超过 3
            int minorDiff = Math.Abs(v1.Value.Minor - v2.Value.Minor);
            if (minorDiff > 3) return false;

            return true;
        }

        /// <summary>
        /// 解析版本号字符串
        /// </summary>
        private static (int Major, int Minor, int Patch)? ParseVersion(string version)
        {
            try
            {
                var parts = version.Split('.');
                if (parts.Length < 2) return null;

                int major = int.Parse(parts[0]);
                int minor = int.Parse(parts[1]);
                int patch = parts.Length >= 3 && int.TryParse(parts[2], out int p) ? p : 0;

                return (major, minor, patch);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 下载 OptiFine 作为 mod 到指定目录
        /// </summary>
        /// <param name="optifineVersion">OptiFine 版本信息</param>
        /// <param name="modsDirectory">mods 文件夹路径</param>
        /// <param name="progressCallback">进度回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async System.Threading.Tasks.Task<bool> DownloadOptiFineAsMod(
            OptifineVersionModel optifineVersion,
            string modsDirectory,
            Action<string, double>? progressCallback = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"[Compatibility] 开始下载 OptiFine 作为 mod: {optifineVersion.Filename}");

                // 确保 mods 目录存在
                if (!Directory.Exists(modsDirectory))
                {
                    Directory.CreateDirectory(modsDirectory);
                }

                var modFilePath = Path.Combine(modsDirectory, optifineVersion.Filename);

                // 如果文件已存在，跳过下载
                if (File.Exists(modFilePath))
                {
                    Debug.WriteLine($"[Compatibility] OptiFine mod 已存在: {modFilePath}");
                    progressCallback?.Invoke("OptiFine 已存在", 100);
                    return true;
                }

                progressCallback?.Invoke("正在下载 OptiFine...", 0);

                // 使用 OptiFineService 下载
                var optifineService = new OptiFineService(DownloadSourceManager.Instance);
                await optifineService.DownloadOptifineInstallerAsync(
                    optifineVersion,
                    modFilePath,
                    (status, current, total, bytes, totalBytes) =>
                    {
                        progressCallback?.Invoke(status, current);
                    },
                    cancellationToken
                );

                Debug.WriteLine($"[Compatibility] OptiFine mod 下载完成: {modFilePath}");
                progressCallback?.Invoke("OptiFine 下载完成", 100);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Compatibility] 下载 OptiFine mod 失败: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 兼容性检查结果
    /// </summary>
    public class CompatibilityResult
    {
        /// <summary>
        /// 是否兼容
        /// </summary>
        public bool IsCompatible { get; set; }

        /// <summary>
        /// 安装模式
        /// </summary>
        public InstallMode InstallMode { get; set; } = InstallMode.AsMod;

        /// <summary>
        /// 原因说明
        /// </summary>
        public string Reason { get; set; } = "";

        /// <summary>
        /// 警告信息（可选）
        /// </summary>
        public string? WarningMessage { get; set; }
    }

    /// <summary>
    /// OptiFine 安装模式
    /// </summary>
    public enum InstallMode
    {
        /// <summary>
        /// 作为 mod 安装到 mods 文件夹（推荐，1.13+）
        /// </summary>
        AsMod,

        /// <summary>
        /// 集成安装（修改版本 JSON，1.12.2 及以下）
        /// </summary>
        Integrated
    }
}

