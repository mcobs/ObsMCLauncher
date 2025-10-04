using System;
using System.IO;
using System.Text;
using System.Windows;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

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
                
                try
                {
                    Console.WriteLine($"启动器已启动，当前下载源: {config.DownloadSource}");
                }
                catch { }
                
                System.Diagnostics.Debug.WriteLine($"启动器已启动，当前下载源: {config.DownloadSource}");
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
    }
}

