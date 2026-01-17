using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ObsMCLauncher.Windows
{
    public partial class CrashWindow : Window
    {
        private readonly string _crashReport;

        public CrashWindow(string summary, string crashReport)
        {
            InitializeComponent();

            SummaryText.Text = summary;
            DetailTextBox.Text = crashReport;
            _crashReport = crashReport;

            LogPathText.Text = "不会自动保存日志，你可以点击右侧按钮导出。";
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_crashReport ?? string.Empty);
            }
            catch
            {
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log",
                DefaultExt = "log"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(saveDialog.FileName, _crashReport ?? string.Empty);
                    LogPathText.Text = $"已导出到：{saveDialog.FileName}";
                }
                catch (Exception ex)
                {
                    LogPathText.Text = $"导出失败：{ex.Message}";
                }
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Application.Current.Shutdown(-1);
            }
            catch
            {
                Environment.Exit(-1);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

