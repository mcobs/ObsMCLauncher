namespace ObsMCLauncher.Models
{
    /// <summary>
    /// 游戏目录类型，决定游戏在哪里运行以及 mods 等文件的位置
    /// </summary>
    public enum GameDirectoryType
    {
        /// <summary>
        /// 根目录模式 - 所有版本共享 .minecraft/mods 等文件夹
        /// </summary>
        RootFolder,

        /// <summary>
        /// 版本独立模式 - 每个版本使用独立的 .minecraft/versions/{version}/mods 文件夹
        /// </summary>
        VersionFolder
    }
}

