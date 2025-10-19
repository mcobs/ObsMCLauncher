using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using ObsMCLauncher.Models;
using ObsMCLauncher.Services;

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

                // 加载配置并初始化下载源
                var config = LauncherConfig.Load();
                DownloadSourceManager.Instance.SetDownloadSource(config.DownloadSource);
                
                // 应用主题
                ApplyTheme(config.ThemeMode);
                
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

                // 更新动态资源
                if (isDark)
                {
                    app.Resources["BackgroundBrush"] = app.Resources["DarkBackgroundBrush"];
                    app.Resources["SurfaceBrush"] = app.Resources["DarkSurfaceBrush"];
                    app.Resources["SurfaceElevatedBrush"] = app.Resources["DarkSurfaceElevatedBrush"];
                    app.Resources["SurfaceHoverBrush"] = app.Resources["DarkSurfaceHoverBrush"];
                    app.Resources["TextBrush"] = app.Resources["DarkTextBrush"];
                    app.Resources["TextSecondaryBrush"] = app.Resources["DarkTextSecondaryBrush"];
                    app.Resources["TextTertiaryBrush"] = app.Resources["DarkTextTertiaryBrush"];
                    app.Resources["BorderBrush"] = app.Resources["DarkBorderBrush"];
                    app.Resources["DividerBrush"] = app.Resources["DarkDividerBrush"];
                    app.Resources["InputBackgroundBrush"] = app.Resources["DarkInputBackgroundBrush"];
                    app.Resources["InputForegroundBrush"] = app.Resources["DarkInputForegroundBrush"];
                    app.Resources["TooltipBackgroundBrush"] = app.Resources["DarkTooltipBackgroundBrush"];
                    app.Resources["TooltipForegroundBrush"] = new SolidColorBrush(Colors.White);
                    app.Resources["TooltipBorderBrush"] = app.Resources["DarkBorderBrush"];
                }
                else
                {
                    app.Resources["BackgroundBrush"] = app.Resources["LightBackgroundBrush"];
                    app.Resources["SurfaceBrush"] = app.Resources["LightSurfaceBrush"];
                    app.Resources["SurfaceElevatedBrush"] = app.Resources["LightSurfaceElevatedBrush"];
                    app.Resources["SurfaceHoverBrush"] = app.Resources["LightSurfaceHoverBrush"];
                    app.Resources["TextBrush"] = app.Resources["LightTextBrush"];
                    app.Resources["TextSecondaryBrush"] = app.Resources["LightTextSecondaryBrush"];
                    app.Resources["TextTertiaryBrush"] = app.Resources["LightTextTertiaryBrush"];
                    app.Resources["BorderBrush"] = app.Resources["LightBorderBrush"];
                    app.Resources["DividerBrush"] = app.Resources["LightDividerBrush"];
                    app.Resources["InputBackgroundBrush"] = app.Resources["LightInputBackgroundBrush"];
                    app.Resources["InputForegroundBrush"] = app.Resources["LightInputForegroundBrush"];
                    app.Resources["TooltipBackgroundBrush"] = app.Resources["LightTooltipBackgroundBrush"];
                    app.Resources["TooltipForegroundBrush"] = app.Resources["LightTextBrush"];
                    app.Resources["TooltipBorderBrush"] = app.Resources["LightBorderBrush"];
                }

                System.Diagnostics.Debug.WriteLine("[App] ✅ 主题切换完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] ❌ 主题切换失败: {ex.Message}");
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

