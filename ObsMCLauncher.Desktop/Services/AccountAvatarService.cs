using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Services;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Desktop.Services;

/// <summary>
/// 账号头像加载服务：从皮肤文件渲染头部头像，失败时回退默认图标。
/// 将 UI 相关逻辑（Bitmap / AssetLoader / 皮肤渲染）从 ViewModel 中剥离。
/// </summary>
public static class AccountAvatarService
{
    /// <summary>
    /// 加载账号皮肤头部头像（后台线程调用安全）。
    /// </summary>
    /// <param name="acc">账号</param>
    /// <param name="forceRefresh">强制重新获取皮肤（刷新操作时用）</param>
    public static async Task<Bitmap?> LoadHeadAsync(GameAccount acc, bool forceRefresh = false)
    {
        try
        {
            var skinPath = await SkinService.Instance.GetSkinPathAsync(acc, forceRefresh);
            if (!string.IsNullOrEmpty(skinPath) && File.Exists(skinPath))
            {
                return SkinHeadRenderer.GetHeadFromSkin(skinPath);
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// 加载默认回退头像（离线/无皮肤账号）。
    /// </summary>
    public static Bitmap? LoadFallbackAvatar()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ObsMCLauncher.Desktop/Assets/logo.png"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
