using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;
using ObsMCLauncher.Utils;

namespace ObsMCLauncher
{
    public partial class App : Application
    {
        public App()
        {
            // 全局异常处理
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        /// <summary>
        /// 配置全局 SSL/TLS 设置，确保能够连接到 Mojang 和其他服务器
        /// </summary>
        private static void ConfigureGlobalSslSettings()
        {
            try
            {
                // 强制使用 TLS 1.2 和 TLS 1.3（推荐的安全协议）
                System.Net.ServicePointManager.SecurityProtocol = 
                    System.Net.SecurityProtocolType.Tls12 | 
                    System.Net.SecurityProtocolType.Tls13;
                
                // 配置连接限制
                System.Net.ServicePointManager.DefaultConnectionLimit = 10;
                System.Net.ServicePointManager.Expect100Continue = false;
                
                // 配置证书验证（生产环境应该启用严格验证）
                // 注意：在开发/测试环境中，如果遇到证书问题，可以临时放宽验证
#if DEBUG
                System.Net.ServicePointManager.ServerCertificateValidationCallback = 
                    (sender, certificate, chain, sslPolicyErrors) => 
                    {
                        // 在调试模式下，记录证书问题但仍然允许连接
                        if (sslPolicyErrors != System.Net.Security.SslPolicyErrors.None)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SSL] 证书验证警告: {sslPolicyErrors}");
                        }
                        return true; // 开发环境：忽略证书错误
                    };
#else
                // 生产环境：使用默认的严格证书验证
                System.Net.ServicePointManager.ServerCertificateValidationCallback = null;
#endif
                
                System.Diagnostics.Debug.WriteLine("[App] ✅ 全局 SSL/TLS 设置已配置");
                System.Diagnostics.Debug.WriteLine($"[App] 支持的协议: {System.Net.ServicePointManager.SecurityProtocol}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ⚠️ SSL/TLS 配置失败: {ex.Message}");
                // 不抛出异常，让应用继续运行
            }
        }

        /// <summary>
        /// 确保OMCL目录存在
        /// </summary>
        private static void EnsureOMCLDirectories()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var configDir = Path.Combine(baseDir, "OMCL", "config");
                var pluginsDir = Path.Combine(baseDir, "OMCL", "plugins");

                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                    System.Diagnostics.Debug.WriteLine($"✅ 已创建配置目录: {configDir}");
                }

                if (!Directory.Exists(pluginsDir))
                {
                    Directory.CreateDirectory(pluginsDir);
                    System.Diagnostics.Debug.WriteLine($"✅ 已创建插件目录: {pluginsDir}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 创建OMCL目录失败: {ex.Message}");
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                // ========== 配置全局 SSL/TLS 设置 ==========
                ConfigureGlobalSslSettings();

                // 尝试设置控制台编码为UTF-8（仅在有控制台窗口时）
                try
                {
                    Console.OutputEncoding = Encoding.UTF8;
                    Console.InputEncoding = Encoding.UTF8;
                    Console.WriteLine("========== 启动器正在启动 ==========");
                }
                catch
                {
                    // WPF应用可能没有控制台窗口，忽略此错误
                }

                // 确保OMCL目录存在
                EnsureOMCLDirectories();
                
                // 加载配置并初始化下载源
                var config = LauncherConfig.Load();
                DownloadSourceManager.Instance.SetDownloadSource(config.DownloadSource);
                
                // 释放MOD翻译文件到运行目录
                ExtractModTranslations();
                
                // 应用主题
                ApplyTheme(config.ThemeMode);
                
                // 启动图片缓存自动清理（降低内存占用）
                ImageCacheManager.StartAutoCleanup();
                
                try
                {
                    Console.WriteLine($"启动器已启动，当前下载源: {config.DownloadSource}");
                }
                catch { }
                
                System.Diagnostics.Debug.WriteLine($"启动器已启动，当前下载源: {config.DownloadSource}");
                
                // 启动时检查更新（异步，不阻塞启动）
                _ = UpdateService.CheckUpdateOnStartupAsync();
            }
            catch (Exception ex)
            {
                var errorMsg = $"启动失败: {ex.Message}\n\n堆栈跟踪:\n{ex.StackTrace}";
                
                try
                {
                    Console.WriteLine($"❌ {errorMsg}");
                }
                catch { }
                
                // 写入错误日志文件
                try
                {
                    File.WriteAllText("startup_error.log", $"{DateTime.Now}\n{errorMsg}\n\n{ex}");
                }
                catch { }
                
                MessageBox.Show(errorMsg, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            var errorMsg = $"发生未处理的异常:\n{e.Exception.Message}\n\n堆栈跟踪:\n{e.Exception.StackTrace}";
            
            try
            {
                Console.WriteLine($"❌ {errorMsg}");
            }
            catch { }
            
            // 写入错误日志文件
            try
            {
                File.WriteAllText("runtime_error.log", $"{DateTime.Now}\n{errorMsg}\n\n{e.Exception}");
            }
            catch { }
            
            MessageBox.Show(errorMsg, "运行时错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // 防止应用崩溃
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            var errorMsg = $"发生致命错误:\n{ex?.Message ?? "未知错误"}\n\n堆栈跟踪:\n{ex?.StackTrace ?? "无"}";
            
            try
            {
                Console.WriteLine($"❌ {errorMsg}");
            }
            catch { }
            
            // 写入错误日志文件
            try
            {
                File.WriteAllText("fatal_error.log", $"{DateTime.Now}\n{errorMsg}\n\n{ex}");
            }
            catch { }
            
            MessageBox.Show(errorMsg, "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// 应用主题
        /// </summary>
        /// <param name="themeMode">0=深色，1=浅色，2=跟随系统</param>
        public static void ApplyTheme(int themeMode)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                bool isDark = themeMode switch
                {
                    0 => true,  // 深色
                    1 => false, // 浅色
                    2 => IsSystemDarkMode(), // 跟随系统
                    _ => true
                };

                System.Diagnostics.Debug.WriteLine($"[App] 切换主题: {(isDark ? "深色" : "浅色")}模式 (设置值: {themeMode})");

                // 更新 MaterialDesign 主题
                var paletteHelper = new PaletteHelper();
                var theme = paletteHelper.GetTheme();
                theme.SetBaseTheme(isDark ? Theme.Dark : Theme.Light);
                paletteHelper.SetTheme(theme);

                // MaterialDesign 可能会冻结/密封资源，这里重新确保动态Brush为可变对象
                // 注意：调用后需要重新从 Resources 取一次引用，避免拿到旧实例
                InitializeDynamicBrushes();

                // 主题资源更新：MaterialDesign 会冻结/密封部分 Brush，导致 ColorAnimation 不稳定
                // 主题切换动画已改由全局遮罩（ThemeTransitionManager）负责，这里只做无动画的资源同步
                if (isDark)
                {
                    UpdateBrushColor("BackgroundBrush", "DarkBackgroundBrush");
                    UpdateBrushColor("SurfaceBrush", "DarkSurfaceBrush");
                    UpdateBrushColor("SurfaceElevatedBrush", "DarkSurfaceElevatedBrush");
                    UpdateBrushColor("SurfaceHoverBrush", "DarkSurfaceHoverBrush");
                    UpdateBrushColor("TextBrush", "DarkTextBrush");
                    UpdateBrushColor("TextSecondaryBrush", "DarkTextSecondaryBrush");
                    UpdateBrushColor("TextTertiaryBrush", "DarkTextTertiaryBrush");
                    UpdateBrushColor("BorderBrush", "DarkBorderBrush");
                    UpdateBrushColor("DividerBrush", "DarkDividerBrush");
                    UpdateBrushColor("InputBackgroundBrush", "DarkInputBackgroundBrush");
                    UpdateBrushColor("InputForegroundBrush", "DarkInputForegroundBrush");
                    UpdateBrushColor("TooltipBackgroundBrush", "DarkTooltipBackgroundBrush");
                    UpdateBrushColor("TooltipForegroundBrush", "DarkTextBrush");
                    UpdateBrushColor("TooltipBorderBrush", "DarkBorderBrush");

                    UpdateGlassmorphismBrush("GlassmorphismBackgroundBrush", "DarkSurfaceBrush", 224);
                    UpdateGlassmorphismBorderBrush("GlassmorphismBorderBrush", true);

                    UpdateSkeletonBrushes(true);
                }
                else
                {
                    UpdateBrushColor("BackgroundBrush", "LightBackgroundBrush");
                    UpdateBrushColor("SurfaceBrush", "LightSurfaceBrush");
                    UpdateBrushColor("SurfaceElevatedBrush", "LightSurfaceElevatedBrush");
                    UpdateBrushColor("SurfaceHoverBrush", "LightSurfaceHoverBrush");
                    UpdateBrushColor("TextBrush", "LightTextBrush");
                    UpdateBrushColor("TextSecondaryBrush", "LightTextSecondaryBrush");
                    UpdateBrushColor("TextTertiaryBrush", "LightTextTertiaryBrush");
                    UpdateBrushColor("BorderBrush", "LightBorderBrush");
                    UpdateBrushColor("DividerBrush", "LightDividerBrush");
                    UpdateBrushColor("InputBackgroundBrush", "LightInputBackgroundBrush");
                    UpdateBrushColor("InputForegroundBrush", "LightInputForegroundBrush");
                    UpdateBrushColor("TooltipBackgroundBrush", "LightTooltipBackgroundBrush");
                    UpdateBrushColor("TooltipForegroundBrush", "LightTextBrush");
                    UpdateBrushColor("TooltipBorderBrush", "LightBorderBrush");

                    UpdateGlassmorphismBrush("GlassmorphismBackgroundBrush", "LightSurfaceBrush", 224);
                    UpdateGlassmorphismBorderBrush("GlassmorphismBorderBrush", false);

                    UpdateSkeletonBrushes(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 主题切换失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化动态Brush资源，确保它们是可变的（非冻结的）
        /// 这样在主题切换时，更新Color属性会自动触发所有使用DynamicResource的元素更新
        /// </summary>
        public static void InitializeDynamicBrushes()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                // 需要确保可变的动态资源列表
                var dynamicBrushKeys = new[]
                {
                    "BackgroundBrush", "SurfaceBrush", "SurfaceElevatedBrush", "SurfaceHoverBrush",
                    "TextBrush", "TextSecondaryBrush", "TextTertiaryBrush",
                    "BorderBrush", "DividerBrush",
                    "InputBackgroundBrush", "InputForegroundBrush",
                    "TooltipBackgroundBrush", "TooltipForegroundBrush", "TooltipBorderBrush",
                    "GlassmorphismBackgroundBrush", "GlassmorphismBorderBrush", // 玻璃态效果资源
                    "SkeletonBackgroundBrush", "SkeletonFillBrush" // 骨架屏资源
                };

                foreach (var key in dynamicBrushKeys)
                {
                    if (app.Resources[key] is SolidColorBrush brush)
                    {
                        // 始终替换为新的可变Brush，避免被第三方主题库密封/冻结后无法再次动画
                        // 保留当前颜色（而不是回退到某个默认色），确保切换过程连续
                        app.Resources[key] = new SolidColorBrush(brush.Color);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[App] ✅ 动态资源初始化完成 (共 {dynamicBrushKeys.Length} 个)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 初始化动态Brush失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新Brush资源的颜色
        /// 直接修改现有Brush的Color属性，这样所有使用DynamicResource的元素会自动更新
        /// </summary>
        private static void UpdateBrushColor(string targetKey, string sourceKey)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var sourceBrush = app.Resources[sourceKey] as SolidColorBrush;
                if (sourceBrush == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] ⚠️ 源Brush不存在: {sourceKey}");
                    return;
                }

                var targetBrush = app.Resources[targetKey] as SolidColorBrush;
                if (targetBrush == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] ⚠️ 目标Brush不存在: {targetKey}");
                    return;
                }

                // 检查是否被冻结
                if (targetBrush.IsFrozen)
                {
                    // 如果被冻结，创建新的可变Brush（不应该发生，因为我们在InitializeDynamicBrushes中已经处理了）
                    var newBrush = new SolidColorBrush(sourceBrush.Color);
                    app.Resources[targetKey] = newBrush;
                }
                else
                {
                    // 直接更新颜色 - 这会触发所有使用此资源的元素更新
                    targetBrush.Color = sourceBrush.Color;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 更新Brush颜色失败 ({targetKey}): {ex.Message}");
            }
        }

        private static Duration GetThemeTransitionDuration(Application app)
        {
            try
            {
                // 兼容系统“减少动画/减少动态效果”设置：开启时直接禁用过渡
                // WPF 对应系统参数：ClientAreaAnimation
                if (!SystemParameters.ClientAreaAnimation)
                {
                    return new Duration(TimeSpan.Zero);
                }

                if (app.Resources["AnimationDuration"] is Duration d)
                {
                    return d;
                }
            }
            catch
            {
            }

            return new Duration(TimeSpan.FromMilliseconds(200));
        }

        private static void AnimateBrushColor(string targetKey, string sourceKey, Duration duration)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var sourceBrush = app.Resources[sourceKey] as SolidColorBrush;
                if (sourceBrush == null) return;

                // 每次都重新获取最新的资源实例，因为它可能已被第三方库替换
                var targetBrush = app.Resources[targetKey] as SolidColorBrush;
                if (targetBrush == null) return;

                // 某些第三方主题库会让Brush处于 sealed 状态（并不一定 IsFrozen==true）
                // 对 sealed/frozen 的 Brush，必须先替换为新的可变实例
                if (targetBrush.IsFrozen || targetBrush.IsSealed)
                {
                    app.Resources[targetKey] = new SolidColorBrush(targetBrush.Color);
                    targetBrush = (SolidColorBrush)app.Resources[targetKey];
                }

                if (duration.TimeSpan == TimeSpan.Zero)
                {
                    targetBrush.Color = sourceBrush.Color;
                    return;
                }

                var animation = new ColorAnimation
                {
                    To = sourceBrush.Color,
                    Duration = duration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                // 直接对 Brush 做 BeginAnimation，避免创建/管理全局 Storyboard
                targetBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 动画更新Brush颜色失败 ({targetKey}): {ex.Message}");
            }
        }

        private static void AnimateGlassmorphismBrush(string targetKey, string sourceKey, byte alpha, Duration duration)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var sourceBrush = app.Resources[sourceKey] as SolidColorBrush;
                if (sourceBrush == null) return;

                var targetBrush = app.Resources[targetKey] as SolidColorBrush;
                if (targetBrush == null) return;

                if (targetBrush.IsFrozen || targetBrush.IsSealed)
                {
                    app.Resources[targetKey] = new SolidColorBrush(targetBrush.Color);
                    targetBrush = (SolidColorBrush)app.Resources[targetKey];
                }

                var toColor = Color.FromArgb(alpha, sourceBrush.Color.R, sourceBrush.Color.G, sourceBrush.Color.B);

                if (duration.TimeSpan == TimeSpan.Zero)
                {
                    targetBrush.Color = toColor;
                    return;
                }

                if (targetBrush.IsFrozen || targetBrush.IsSealed)
                {
                    app.Resources[targetKey] = new SolidColorBrush(targetBrush.Color);
                    targetBrush = (SolidColorBrush)app.Resources[targetKey];
                }

                var animation = new ColorAnimation
                {
                    To = toColor,
                    Duration = duration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                targetBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 动画更新玻璃态Brush失败 ({targetKey}): {ex.Message}");
            }
        }

        private static void AnimateGlassmorphismBorderBrush(string targetKey, bool isDark, Duration duration)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var targetBrush = app.Resources[targetKey] as SolidColorBrush;
                if (targetBrush == null) return;

                if (targetBrush.IsFrozen || targetBrush.IsSealed)
                {
                    app.Resources[targetKey] = new SolidColorBrush(targetBrush.Color);
                    targetBrush = (SolidColorBrush)app.Resources[targetKey];
                }

                var toColor = isDark
                    ? Color.FromArgb(128, 255, 255, 255)
                    : Color.FromArgb(76, 0, 0, 0);

                if (duration.TimeSpan == TimeSpan.Zero)
                {
                    targetBrush.Color = toColor;
                    return;
                }

                if (targetBrush.IsFrozen)
                {
                    app.Resources[targetKey] = new SolidColorBrush(targetBrush.Color);
                    targetBrush = (SolidColorBrush)app.Resources[targetKey];
                }

                var animation = new ColorAnimation
                {
                    To = toColor,
                    Duration = duration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                targetBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 动画更新玻璃态边框Brush失败 ({targetKey}): {ex.Message}");
            }
        }

        private static void AnimateSkeletonBrushes(bool isDark, Duration duration)
        {
            try
            {
                if (isDark)
                {
                    AnimateBrushColor("SkeletonBackgroundBrush", "DarkSurfaceHoverBrush", duration);
                    AnimateBrushColor("SkeletonFillBrush", "DarkSurfaceHoverBrush", duration);
                }
                else
                {
                    AnimateBrushColor("SkeletonBackgroundBrush", "LightSurfaceHoverBrush", duration);
                    AnimateBrushColor("SkeletonFillBrush", "LightSurfaceHoverBrush", duration);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 动画更新骨架屏资源失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新玻璃态背景Brush（基于SurfaceBrush的半透明版本）
        /// </summary>
        private static void UpdateGlassmorphismBrush(string targetKey, string sourceKey, byte alpha)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var sourceBrush = app.Resources[sourceKey] as SolidColorBrush;
                if (sourceBrush == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] ⚠️ 源Brush不存在: {sourceKey}");
                    return;
                }

                var targetBrush = app.Resources[targetKey] as SolidColorBrush;
                if (targetBrush == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] ⚠️ 目标Brush不存在: {targetKey}");
                    return;
                }

                // 创建半透明颜色（保持RGB，修改Alpha）
                var glassColor = Color.FromArgb(alpha, sourceBrush.Color.R, sourceBrush.Color.G, sourceBrush.Color.B);

                // 检查是否被冻结
                if (targetBrush.IsFrozen)
                {
                    var newBrush = new SolidColorBrush(glassColor);
                    app.Resources[targetKey] = newBrush;
                }
                else
                {
                    targetBrush.Color = glassColor;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 更新玻璃态Brush失败 ({targetKey}): {ex.Message}");
            }
        }

        /// <summary>
        /// 更新玻璃态边框Brush（根据主题调整高光效果）
        /// </summary>
        private static void UpdateGlassmorphismBorderBrush(string targetKey, bool isDark)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var targetBrush = app.Resources[targetKey] as SolidColorBrush;
                if (targetBrush == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] ⚠️ 目标Brush不存在: {targetKey}");
                    return;
                }

                // 深色主题：白色高光（50%不透明度）
                // 浅色主题：深色高光（30%不透明度）
                var borderColor = isDark 
                    ? Color.FromArgb(128, 255, 255, 255)  // 深色：白色高光
                    : Color.FromArgb(76, 0, 0, 0);         // 浅色：深色高光（30%不透明度）

                // 检查是否被冻结
                if (targetBrush.IsFrozen)
                {
                    var newBrush = new SolidColorBrush(borderColor);
                    app.Resources[targetKey] = newBrush;
                }
                else
                {
                    targetBrush.Color = borderColor;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 更新玻璃态边框Brush失败 ({targetKey}): {ex.Message}");
            }
        }

        /// <summary>
        /// 更新骨架屏资源（根据主题设置颜色）
        /// </summary>
        private static void UpdateSkeletonBrushes(bool isDark)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                if (isDark)
                {
                    // 深色主题：使用深色
                    UpdateBrushColor("SkeletonBackgroundBrush", "DarkSurfaceHoverBrush");
                    UpdateBrushColor("SkeletonFillBrush", "DarkSurfaceHoverBrush");
                }
                else
                {
                    // 浅色主题：使用浅色
                    UpdateBrushColor("SkeletonBackgroundBrush", "LightSurfaceHoverBrush");
                    UpdateBrushColor("SkeletonFillBrush", "LightSurfaceHoverBrush");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 更新骨架屏资源失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放MOD翻译文件到运行目录\OMCL\
        /// </summary>
        private static void ExtractModTranslations()
        {
            try
            {
                // 目标目录和文件路径
                var omclDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL");
                var targetPath = Path.Combine(omclDir, "mod_translations.txt");

                // 如果文件已存在，不覆盖（允许用户自定义编辑）
                if (File.Exists(targetPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[App] MOD翻译文件已存在，跳过释放: {targetPath}");
                    return;
                }

                // 确保目录存在
                Directory.CreateDirectory(omclDir);

                // 从嵌入资源读取
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "ObsMCLauncher.Assets.mod_translations.txt";
                
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] ⚠️ 未找到嵌入资源: {resourceName}");
                    
                    // 列出所有嵌入资源用于调试
                    var allResources = assembly.GetManifestResourceNames();
                    System.Diagnostics.Debug.WriteLine("[App] 可用的嵌入资源:");
                    foreach (var name in allResources)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {name}");
                    }
                    return;
                }

                // 写入文件
                using var fileStream = File.Create(targetPath);
                stream.CopyTo(fileStream);
                
                System.Diagnostics.Debug.WriteLine($"[App] ✅ MOD翻译文件已释放: {targetPath}");
                System.Diagnostics.Debug.WriteLine($"[App] 文件大小: {new FileInfo(targetPath).Length / 1024.0:F2} KB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 释放MOD翻译文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 检测系统是否为深色模式
        /// </summary>
        private static bool IsSystemDarkMode()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("AppsUseLightTheme");
                return value is int intValue && intValue == 0;
            }
            catch
            {
                return true; // 默认深色
            }
        }
    }
}

