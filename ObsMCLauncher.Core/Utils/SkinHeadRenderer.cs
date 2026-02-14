using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ObsMCLauncher.Core.Utils;

/// <summary>
/// 皮肤头像渲染器 - 适配 Avalonia
/// </summary>
public static class SkinHeadRenderer
{
    /// <summary>
    /// 从皮肤文件提取头像并返回 Bitmap
    /// </summary>
    public static Bitmap? GetHeadFromSkin(string skinPath, int size = 64)
    {
        try
        {
            if (!File.Exists(skinPath)) return null;

            using var stream = File.OpenRead(skinPath);
            // 关键修复：直接加载为 WriteableBitmap，这样才有 Lock 方法
            var skinBitmap = WriteableBitmap.Decode(stream);

            // 检查皮肤尺寸
            int skinWidth = (int)skinBitmap.Size.Width;
            int skinHeight = (int)skinBitmap.Size.Height;

            // 创建输出的 WriteableBitmap
            var result = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

            using (var lockedSkin = skinBitmap.Lock())
            using (var lockedResult = result.Lock())
            {
                unsafe
                {
                    uint* skinPtr = (uint*)lockedSkin.Address;
                    uint* resultPtr = (uint*)lockedResult.Address;

                    double scale = (double)size / 8.0;

                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            int srcX = (int)(x / scale);
                            int srcY = (int)(y / scale);
                            srcX = Math.Min(srcX, 7);
                            srcY = Math.Min(srcY, 7);

                            // 基础头部坐标 (8, 8)
                            uint headPixel = GetPixel(skinPtr, 8 + srcX, 8 + srcY, skinWidth);
                            
                            // 帽子层坐标 (40, 8) - 仅 64x64 皮肤支持
                            uint finalPixel = headPixel;
                            if (skinHeight >= 64)
                            {
                                uint hatPixel = GetPixel(skinPtr, 40 + srcX, 8 + srcY, skinWidth);
                                finalPixel = BlendPixels(headPixel, hatPixel);
                            }

                            resultPtr[y * size + x] = finalPixel;
                        }
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SkinHeadRenderer] 渲染失败: {ex.Message}");
            return null;
        }
    }

    private static unsafe uint GetPixel(uint* ptr, int x, int y, int width)
    {
        return ptr[y * width + x];
    }

    private static uint BlendPixels(uint background, uint foreground)
    {
        uint alpha = (foreground >> 24) & 0xFF;
        if (alpha == 0) return background;
        if (alpha == 255) return foreground;

        // 简化的 Alpha 混合 (Bgra8888)
        float a = alpha / 255.0f;
        float invA = 1.0f - a;

        uint b = (uint)((background & 0xFF) * invA + (foreground & 0xFF) * a);
        uint g = (uint)(((background >> 8) & 0xFF) * invA + ((foreground >> 8) & 0xFF) * a);
        uint r = (uint)(((background >> 16) & 0xFF) * invA + ((foreground >> 16) & 0xFF) * a);

        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }
}
