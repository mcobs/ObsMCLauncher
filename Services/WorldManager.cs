using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 世界/存档管理器
    /// </summary>
    public class WorldManager
    {
        private static WorldManager? _instance;
        public static WorldManager Instance => _instance ??= new WorldManager();

        private WorldManager() { }

        /// <summary>
        /// 获取所有世界列表
        /// </summary>
        /// <param name="gameDirectory">游戏目录（会自动添加saves子目录）或saves目录路径</param>
        public List<WorldInfo> GetWorlds(string gameDirectory)
        {
            var worlds = new List<WorldInfo>();
            
            // 判断是游戏目录还是saves目录
            string savesDir;
            if (Path.GetFileName(gameDirectory).Equals("saves", StringComparison.OrdinalIgnoreCase) ||
                Directory.Exists(Path.Combine(gameDirectory, "saves")))
            {
                // 如果传入的是saves目录，直接使用；否则添加saves子目录
                savesDir = Directory.Exists(Path.Combine(gameDirectory, "saves"))
                    ? Path.Combine(gameDirectory, "saves")
                    : gameDirectory;
            }
            else
            {
                savesDir = Path.Combine(gameDirectory, "saves");
            }

            if (!Directory.Exists(savesDir))
                return worlds;

            try
            {
                var worldDirs = Directory.GetDirectories(savesDir);
                foreach (var worldDir in worldDirs)
                {
                    var worldName = Path.GetFileName(worldDir);
                    var levelDatPath = Path.Combine(worldDir, "level.dat");

                    // 检查是否是有效的世界（必须有level.dat）
                    if (!File.Exists(levelDatPath))
                        continue;

                    var worldInfo = new WorldInfo
                    {
                        Name = worldName,
                        FullPath = worldDir,
                        LastPlayed = Directory.GetLastWriteTime(worldDir),
                        Size = CalculateDirectorySize(worldDir),
                        ThumbnailPath = GetThumbnailPath(worldDir)
                    };

                    // 尝试读取level.dat获取详细信息
                    try
                    {
                        var levelData = ReadLevelDat(levelDatPath);
                        if (levelData != null)
                        {
                            worldInfo.GameMode = levelData.GameMode;
                            worldInfo.Difficulty = levelData.Difficulty;
                            worldInfo.Seed = levelData.Seed;
                            worldInfo.GameVersion = levelData.GameVersion;
                        }
                    }
                    catch
                    {
                        // 读取失败不影响显示，只显示基本信息
                    }

                    worlds.Add(worldInfo);
                }

                // 按最后游玩时间排序（最新的在前）
                worlds = worlds.OrderByDescending(w => w.LastPlayed).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldManager] 获取世界列表失败: {ex.Message}");
            }

            return worlds;
        }

        /// <summary>
        /// 计算目录大小
        /// </summary>
        private long CalculateDirectorySize(string directory)
        {
            long size = 0;
            try
            {
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        size += fileInfo.Length;
                    }
                    catch
                    {
                        // 忽略无法访问的文件
                    }
                }
            }
            catch
            {
                // 忽略无法访问的目录
            }
            return size;
        }

        /// <summary>
        /// 获取缩略图路径
        /// </summary>
        private string? GetThumbnailPath(string worldDir)
        {
            var iconPath = Path.Combine(worldDir, "icon.png");
            if (File.Exists(iconPath))
                return iconPath;
            return null;
        }

        /// <summary>
        /// 读取level.dat文件
        /// </summary>
        private LevelDatData? ReadLevelDat(string levelDatPath)
        {
            try
            {
                // level.dat是NBT格式，但我们可以尝试读取一些基本信息
                // 这里使用简化的方法，实际应该使用NBT库
                // 为了简化，我们只读取文件修改时间等信息
                var fileInfo = new FileInfo(levelDatPath);
                
                // 注意：完整读取NBT需要专门的库，这里只返回基本信息
                // 实际项目中应该使用类似NBTSharp或MinecraftLevelReader的库
                return new LevelDatData
                {
                    GameMode = "未知",
                    Difficulty = "未知",
                    Seed = null,
                    GameVersion = null
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 备份世界
        /// </summary>
        public bool BackupWorld(WorldInfo world, string backupDirectory)
        {
            try
            {
                if (!Directory.Exists(backupDirectory))
                    Directory.CreateDirectory(backupDirectory);

                var backupName = $"{world.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
                var backupPath = Path.Combine(backupDirectory, backupName);

                CopyDirectory(world.FullPath, backupPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldManager] 备份世界失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 恢复世界
        /// </summary>
        public bool RestoreWorld(string backupPath, string savesDirectory, string? newName = null)
        {
            try
            {
                if (!Directory.Exists(backupPath))
                    return false;

                var worldName = newName ?? Path.GetFileName(backupPath);
                var targetPath = Path.Combine(savesDirectory, worldName);

                // 如果目标已存在，添加时间戳
                if (Directory.Exists(targetPath))
                {
                    worldName = $"{worldName}_{DateTime.Now:yyyyMMdd_HHmmss}";
                    targetPath = Path.Combine(savesDirectory, worldName);
                }

                CopyDirectory(backupPath, targetPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldManager] 恢复世界失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除世界
        /// </summary>
        public bool DeleteWorld(WorldInfo world)
        {
            try
            {
                if (Directory.Exists(world.FullPath))
                {
                    Directory.Delete(world.FullPath, true);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldManager] 删除世界失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 重命名世界
        /// </summary>
        public bool RenameWorld(WorldInfo world, string newName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newName))
                    return false;

                var parentDir = Path.GetDirectoryName(world.FullPath);
                if (string.IsNullOrEmpty(parentDir))
                    return false;

                var newPath = Path.Combine(parentDir, newName);

                if (Directory.Exists(newPath))
                    return false; // 目标名称已存在

                Directory.Move(world.FullPath, newPath);
                world.Name = newName;
                world.FullPath = newPath;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WorldManager] 重命名世界失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 复制目录
        /// </summary>
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                var destDir = Path.Combine(targetDir, dirName);
                CopyDirectory(dir, destDir);
            }
        }

        /// <summary>
        /// Level.dat数据（简化版）
        /// </summary>
        private class LevelDatData
        {
            public string? GameMode { get; set; }
            public string? Difficulty { get; set; }
            public long? Seed { get; set; }
            public string? GameVersion { get; set; }
        }
    }
}

