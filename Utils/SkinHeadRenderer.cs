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
                // 不设置 DecodePixelWidth/Height，保持原始分辨率
                skinBitmap.EndInit();
                skinBitmap.Freeze();

                // 检查皮肤尺寸（标准为 64x64 或 64x32）
                int skinWidth = skinBitmap.PixelWidth;
                int skinHeight = skinBitmap.PixelHeight;

                if (skinWidth != 64 || (skinHeight != 64 && skinHeight != 32))
                {
                    System.Diagnostics.Debug.WriteLine($"[SkinHeadRenderer] 非标准皮肤尺寸: {skinWidth}x{skinHeight}");
                    // 仍然尝试提取，但可能失败
                }

                // 提取基础头部（8x8 像素，位于 (8,8) 到 (16,16)）
                var headRect = new System.Windows.Int32Rect(8, 8, 8, 8);
                var headCropped = new CroppedBitmap(skinBitmap, headRect);

                // 创建缩放后的头部图像
                var headScaled = new TransformedBitmap(headCropped, new ScaleTransform(
                    (double)size / 8.0,
                    (double)size / 8.0
                ));

                // 如果有帽子层，也进行缩放
                BitmapSource? hatScaled = null;
                if (skinHeight >= 64)
                {
                    var hatRect = new System.Windows.Int32Rect(40, 8, 8, 8);
                    var hatCropped = new CroppedBitmap(skinBitmap, hatRect);
                    hatScaled = new TransformedBitmap(hatCropped, new ScaleTransform(
                        (double)size / 8.0,
                        (double)size / 8.0
                    ));
                }

                // 使用标准 DPI 创建渲染目标
                var renderTarget = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                var drawingVisual = new DrawingVisual();

                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // 设置 NearestNeighbor 以保持像素艺术风格（清晰的像素边缘）
                    RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.NearestNeighbor);

                    // 绘制基础头部
                    drawingContext.DrawImage(headScaled, new System.Windows.Rect(0, 0, size, size));

                    // 绘制帽子层（如果存在）
                    if (hatScaled != null)
                    {
                        drawingContext.DrawImage(hatScaled, new System.Windows.Rect(0, 0, size, size));
                    }
                }

                renderTarget.Render(drawingVisual);
                renderTarget.Freeze();

                return renderTarget;
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
