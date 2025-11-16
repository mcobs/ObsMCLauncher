using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// 皮肤头像渲染器 - 从 Minecraft 皮肤提取头像
    /// </summary>
    public static class SkinHeadRenderer
    {
        /// <summary>
        /// 从皮肤文件提取头像（包含帽子层）
        /// </summary>
        /// <param name="skinPath">皮肤文件路径</param>
        /// <param name="size">输出头像尺寸（默认60x60）</param>
        /// <returns>头像 ImageSource，失败返回 null</returns>
        public static ImageSource? GetHeadFromSkin(string skinPath, int size = 60)
        {
            try
            {
                if (!File.Exists(skinPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[SkinHeadRenderer] 皮肤文件不存在: {skinPath}");
                    return null;
                }

                // 加载皮肤图片
                var skinBitmap = new BitmapImage();
                skinBitmap.BeginInit();
                skinBitmap.UriSource = new Uri(skinPath, UriKind.Absolute);
                skinBitmap.CacheOption = BitmapCacheOption.OnLoad;
                skinBitmap.EndInit();
                skinBitmap.Freeze();

                // 检查皮肤尺寸
                int skinWidth = skinBitmap.PixelWidth;
                int skinHeight = skinBitmap.PixelHeight;

                if (skinWidth != 64 || (skinHeight != 64 && skinHeight != 32))
                {
                    System.Diagnostics.Debug.WriteLine($"[SkinHeadRenderer] 非标准皮肤尺寸: {skinWidth}x{skinHeight}");
                }

                // 提取基础头部（8x8 像素，位于 (8,8) 到 (16,16)）
                var headRect = new System.Windows.Int32Rect(8, 8, 8, 8);
                var headCropped = new CroppedBitmap(skinBitmap, headRect);

                // 提取帽子层（如果存在）
                CroppedBitmap? hatCropped = null;
                if (skinHeight >= 64)
                {
                    var hatRect = new System.Windows.Int32Rect(40, 8, 8, 8);
                    hatCropped = new CroppedBitmap(skinBitmap, hatRect);
                }

                // 获取头部像素数据
                int headStride = 8 * 4; // 8 pixels * 4 bytes (BGRA)
                byte[] headPixels = new byte[8 * 8 * 4];
                headCropped.CopyPixels(headPixels, headStride, 0);

                // 获取帽子层像素数据（如果存在）
                byte[]? hatPixels = null;
                if (hatCropped != null)
                {
                    hatPixels = new byte[8 * 8 * 4];
                    hatCropped.CopyPixels(hatPixels, headStride, 0);
                }

                // 创建输出像素数组
                int outputStride = size * 4;
                byte[] outputPixels = new byte[size * size * 4];

                // 计算缩放比例
                double scale = (double)size / 8.0;

                // 逐像素进行最近邻缩放（托管代码版本）
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // 计算源像素坐标（最近邻）
                        int srcX = (int)(x / scale);
                        int srcY = (int)(y / scale);

                        // 确保不越界
                        srcX = Math.Min(srcX, 7);
                        srcY = Math.Min(srcY, 7);

                        int srcIndex = (srcY * 8 + srcX) * 4;
                        int dstIndex = (y * size + x) * 4;

                        // 复制基础头部像素
                        outputPixels[dstIndex] = headPixels[srcIndex];         // B
                        outputPixels[dstIndex + 1] = headPixels[srcIndex + 1]; // G
                        outputPixels[dstIndex + 2] = headPixels[srcIndex + 2]; // R
                        outputPixels[dstIndex + 3] = headPixels[srcIndex + 3]; // A

                        // 叠加帽子层（如果存在且不透明）
                        if (hatPixels != null)
                        {
                            byte hatAlpha = hatPixels[srcIndex + 3];
                            if (hatAlpha > 0)
                            {
                                if (hatAlpha == 255)
                                {
                                    // 完全不透明，直接覆盖
                                    outputPixels[dstIndex] = hatPixels[srcIndex];
                                    outputPixels[dstIndex + 1] = hatPixels[srcIndex + 1];
                                    outputPixels[dstIndex + 2] = hatPixels[srcIndex + 2];
                                    outputPixels[dstIndex + 3] = hatPixels[srcIndex + 3];
                                }
                                else
                                {
                                    // 半透明，进行 Alpha 混合
                                    float alpha = hatAlpha / 255.0f;
                                    float invAlpha = 1.0f - alpha;

                                    outputPixels[dstIndex] = (byte)(hatPixels[srcIndex] * alpha + outputPixels[dstIndex] * invAlpha);
                                    outputPixels[dstIndex + 1] = (byte)(hatPixels[srcIndex + 1] * alpha + outputPixels[dstIndex + 1] * invAlpha);
                                    outputPixels[dstIndex + 2] = (byte)(hatPixels[srcIndex + 2] * alpha + outputPixels[dstIndex + 2] * invAlpha);
                                    outputPixels[dstIndex + 3] = (byte)Math.Max(outputPixels[dstIndex + 3], hatAlpha);
                                }
                            }
                        }
                    }
                }

                // 创建 WriteableBitmap 并写入像素数据
                var writeableBitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
                writeableBitmap.WritePixels(
                    new System.Windows.Int32Rect(0, 0, size, size),
                    outputPixels,
                    outputStride,
                    0
                );

                writeableBitmap.Freeze();
                return writeableBitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinHeadRenderer] 提取头像失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 从皮肤文件提取头像并保存为新文件
        /// </summary>
        /// <param name="skinPath">皮肤文件路径</param>
        /// <param name="outputPath">输出头像路径</param>
        /// <param name="size">输出头像尺寸</param>
        /// <returns>是否成功</returns>
        public static bool SaveHeadFromSkin(string skinPath, string outputPath, int size = 60)
        {
            try
            {
                var headImage = GetHeadFromSkin(skinPath, size);
                if (headImage == null)
                {
                    return false;
                }

                // 保存为 PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)headImage));

                using var fileStream = new FileStream(outputPath, FileMode.Create);
                encoder.Save(fileStream);

                System.Diagnostics.Debug.WriteLine($"[SkinHeadRenderer] 头像已保存: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SkinHeadRenderer] 保存头像失败: {ex.Message}");
                return false;
            }
        }
    }
}
