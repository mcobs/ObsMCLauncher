using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 账号管理服务
    /// </summary>
    public class AccountService
    {
        private static readonly Lazy<AccountService> _instance = new(() => new AccountService());
        public static AccountService Instance => _instance.Value;

        private string _accountsFilePath = string.Empty;
        private List<GameAccount> _accounts = new List<GameAccount>();

        private AccountService()
        {
            ReloadAccountsPath();
        }

        /// <summary>
        /// 重新加载账号文件路径和数据
        /// </summary>
        public void ReloadAccountsPath()
        {
            var config = LauncherConfig.Load();
            var newPath = config.GetAccountFilePath();
            
            // 如果路径变了，需要迁移账号数据
            if (_accountsFilePath != newPath && !string.IsNullOrEmpty(_accountsFilePath))
            {
                MigrateAccounts(_accountsFilePath, newPath);
            }
            
            _accountsFilePath = newPath;
            
            var directory = Path.GetDirectoryName(_accountsFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            _accounts = LoadAccounts();
        }

        /// <summary>
        /// 迁移账号数据到新位置（移动而不是复制）
        /// </summary>
        private void MigrateAccounts(string oldPath, string newPath)
        {
            try
            {
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    var directory = Path.GetDirectoryName(newPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    // 先复制到新位置
                    File.Copy(oldPath, newPath, true);
                    System.Diagnostics.Debug.WriteLine($"✅ 账号数据已复制到: {newPath}");
                    
                    // 验证新文件是否存在且有效
                    if (File.Exists(newPath))
                    {
                        try
                        {
                            // 尝试删除旧文件
                            File.Delete(oldPath);
                            System.Diagnostics.Debug.WriteLine($"✅ 已删除旧账号文件: {oldPath}");
                            System.Diagnostics.Debug.WriteLine($"✅ 账号数据迁移完成: {oldPath} -> {newPath}");
                        }
                        catch (Exception deleteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ 删除旧账号文件失败: {deleteEx.Message}");
                            System.Diagnostics.Debug.WriteLine($"   旧文件位置: {oldPath}");
                            System.Diagnostics.Debug.WriteLine($"   可以手动删除");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 迁移账号数据失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   从: {oldPath}");
                System.Diagnostics.Debug.WriteLine($"   到: {newPath}");
            }
        }

        /// <summary>
        /// 获取所有账号
        /// </summary>
        public List<GameAccount> GetAllAccounts()
        {
            return _accounts.OrderByDescending(a => a.IsDefault)
                           .ThenByDescending(a => a.LastUsed)
                           .ToList();
        }

        /// <summary>
        /// 获取默认账号
        /// </summary>
        public GameAccount? GetDefaultAccount()
        {
            return _accounts.FirstOrDefault(a => a.IsDefault);
        }

        /// <summary>
        /// 添加离线账号
        /// </summary>
        public GameAccount AddOfflineAccount(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("用户名不能为空", nameof(username));
            }

            // 检查用户名是否已存在
            if (_accounts.Any(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"用户名 '{username}' 已存在");
            }

            var account = new GameAccount
            {
                Username = username,
                Type = AccountType.Offline,
                IsDefault = _accounts.Count == 0 // 如果是第一个账号，设为默认
            };

            _accounts.Add(account);
            SaveAccounts();

            return account;
        }

        /// <summary>
        /// 添加微软账号
        /// </summary>
        public GameAccount AddMicrosoftAccount(GameAccount account)
        {
            if (account == null)
            {
                throw new ArgumentNullException(nameof(account));
            }

            // 检查用户名是否已存在
            if (_accounts.Any(a => a.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"账号 '{account.Username}' 已存在");
            }

            // 如果是第一个账号，设为默认
            account.IsDefault = _accounts.Count == 0;

            _accounts.Add(account);
            SaveAccounts();

            return account;
        }

        /// <summary>
        /// 刷新微软账号令牌
        /// </summary>
        public async System.Threading.Tasks.Task<bool> RefreshMicrosoftAccountAsync(string accountId)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId && a.Type == AccountType.Microsoft);
            if (account == null)
            {
                return false;
            }

            var authService = new MicrosoftAuthService();
            var success = await authService.RefreshTokenAsync(account);
            
            if (success)
            {
                SaveAccounts();
            }

            return success;
        }

        /// <summary>
        /// 删除账号
        /// </summary>
        public void DeleteAccount(string accountId)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                _accounts.Remove(account);

                // 如果删除的是默认账号，自动设置第一个账号为默认
                if (account.IsDefault && _accounts.Count > 0)
                {
                    _accounts[0].IsDefault = true;
                }

                SaveAccounts();
            }
        }

        /// <summary>
        /// 设置默认账号
        /// </summary>
        public void SetDefaultAccount(string accountId)
        {
            // 清除所有账号的默认状态
            foreach (var acc in _accounts)
            {
                acc.IsDefault = false;
            }

            // 设置新的默认账号
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                account.IsDefault = true;
                account.LastUsed = DateTime.Now;
                SaveAccounts();
            }
        }

        /// <summary>
        /// 更新账号最后使用时间
        /// </summary>
        public void UpdateLastUsed(string accountId)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                account.LastUsed = DateTime.Now;
                SaveAccounts();
            }
        }

        /// <summary>
        /// 加载账号列表
        /// </summary>
        private List<GameAccount> LoadAccounts()
        {
            try
            {
                if (File.Exists(_accountsFilePath))
                {
                    var json = File.ReadAllText(_accountsFilePath);
                    var accounts = JsonSerializer.Deserialize<List<GameAccount>>(json);
                    return accounts ?? new List<GameAccount>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载账号列表失败: {ex.Message}");
            }

            return new List<GameAccount>();
        }

        /// <summary>
        /// 保存账号列表
        /// </summary>
        private void SaveAccounts()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_accounts, options);
                File.WriteAllText(_accountsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存账号列表失败: {ex.Message}");
            }
        }
    }
}

