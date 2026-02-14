using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services.Accounts;

public class AccountService
{
    private static readonly Lazy<AccountService> _instance = new(() => new AccountService());
    public static AccountService Instance => _instance.Value;

    private string _accountsFilePath = string.Empty;
    private List<GameAccount> _accounts = [];

    private AccountService()
    {
        ReloadAccountsPath();
    }

    public void ReloadAccountsPath()
    {
        var config = LauncherConfig.Load();
        var newPath = config.GetAccountFilePath();

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

                File.Copy(oldPath, newPath, true);

                if (File.Exists(newPath))
                {
                    try
                    {
                        File.Delete(oldPath);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }

    public List<GameAccount> GetAllAccounts()
    {
        return _accounts.OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.LastUsed)
            .ToList();
    }

    public GameAccount? GetDefaultAccount()
    {
        return _accounts.FirstOrDefault(a => a.IsDefault);
    }

    public GameAccount? GetById(string accountId)
    {
        return _accounts.FirstOrDefault(a => a.Id == accountId);
    }

    public GameAccount AddOfflineAccount(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("用户名不能为空", nameof(username));

        if (_accounts.Any(a => a.Type == AccountType.Offline && a.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"用户名 '{username}' 已存在");

        var account = new GameAccount
        {
            Username = username,
            Type = AccountType.Offline,
            IsDefault = _accounts.Count == 0
        };

        _accounts.Add(account);
        SaveAccounts();

        return account;
    }

    public GameAccount AddOrUpdateMicrosoftAccount(GameAccount account)
    {
        if (account.Type != AccountType.Microsoft)
            throw new ArgumentException("账号类型必须是 Microsoft", nameof(account));

        if (string.IsNullOrWhiteSpace(account.Username))
            throw new ArgumentException("用户名不能为空", nameof(account));

        var key = !string.IsNullOrWhiteSpace(account.MinecraftUUID) ? account.MinecraftUUID : account.UUID;
        var existing = _accounts.FirstOrDefault(a => a.Type == AccountType.Microsoft &&
                                                    (!string.IsNullOrWhiteSpace(a.MinecraftUUID) ? a.MinecraftUUID == key : a.UUID == key));

        if (existing == null)
        {
            if (_accounts.Count == 0)
                account.IsDefault = true;

            account.LastUsed = DateTime.Now;
            _accounts.Add(account);
            SaveAccounts();
            return account;
        }

        existing.Username = account.Username;
        existing.Email = account.Email;
        existing.UUID = account.UUID;
        existing.MinecraftUUID = account.MinecraftUUID;
        existing.AccessToken = account.AccessToken;
        existing.RefreshToken = account.RefreshToken;
        existing.ExpiresAt = account.ExpiresAt;
        existing.MinecraftAccessToken = account.MinecraftAccessToken;
        existing.LastUsed = DateTime.Now;

        SaveAccounts();
        return existing;
    }

    public async Task<bool> RefreshMicrosoftAccountAsync(string accountId, Action<string>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var account = GetById(accountId);
        if (account == null) return false;
        if (account.Type != AccountType.Microsoft) return false;

        var svc = new MicrosoftAuthService
        {
            OnProgressUpdate = onProgress
        };

        var ok = await svc.RefreshTokenAsync(account, cancellationToken);
        if (ok)
        {
            account.LastUsed = DateTime.Now;
            SaveAccounts();
        }

        return ok;
    }

    public async Task<bool> EnsureMicrosoftAccountValidAsync(string accountId, Action<string>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var account = GetById(accountId);
        if (account == null) return false;
        if (account.Type != AccountType.Microsoft) return true;

        if (!account.IsTokenExpired())
            return true;

        return await RefreshMicrosoftAccountAsync(accountId, onProgress, cancellationToken);
    }

    public async Task<bool> RefreshYggdrasilAccountAsync(string accountId, Action<string>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var account = GetById(accountId);
        if (account == null) return false;
        if (account.Type != AccountType.Yggdrasil) return false;

        onProgress?.Invoke("正在刷新外置登录令牌...");

        var svc = new ObsMCLauncher.Core.Services.YggdrasilAuthService
        {
            OnProgressUpdate = onProgress
        };

        cancellationToken.ThrowIfCancellationRequested();
        var ok = await svc.RefreshTokenAsync(account).ConfigureAwait(false);
        if (ok)
        {
            account.LastUsed = DateTime.Now;
            SaveAccounts();
        }

        return ok;
    }

    public void DeleteAccount(string accountId)
    {
        var account = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (account != null)
        {
            _accounts.Remove(account);

            if (account.IsDefault && _accounts.Count > 0)
            {
                _accounts[0].IsDefault = true;
            }

            SaveAccounts();
        }
    }

    public void SetDefaultAccount(string accountId)
    {
        foreach (var acc in _accounts)
        {
            acc.IsDefault = false;
        }

        var account = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (account != null)
        {
            account.IsDefault = true;
            account.LastUsed = DateTime.Now;
            SaveAccounts();
        }
    }

    public void UpdateLastUsed(string accountId)
    {
        var account = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (account != null)
        {
            account.LastUsed = DateTime.Now;
            SaveAccounts();
        }
    }

    public void SaveAccountsData()
    {
        SaveAccounts();
    }

    public GameAccount AddYggdrasilAccount(GameAccount account)
    {
        if (account.Type != AccountType.Yggdrasil)
            throw new ArgumentException("账号类型必须是 Yggdrasil", nameof(account));

        if (string.IsNullOrWhiteSpace(account.Username))
            throw new ArgumentException("用户名不能为空", nameof(account));

        // 方案A：检查是否存在相同 用户名 + 服务器组合 的账号 (不区分大小写)
        var existing = _accounts.FirstOrDefault(a => a.Type == AccountType.Yggdrasil && 
                                                      a.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase) &&
                                                      a.YggdrasilServerId == account.YggdrasilServerId);

        if (existing == null)
        {
            // 如果列表里已有同 UUID 的账号（可能来自不同服务器名但同源），也应视为重复或需要更新
            existing = _accounts.FirstOrDefault(a => a.Type == AccountType.Yggdrasil && a.UUID == account.UUID);
        }

        if (existing == null)
        {
            if (_accounts.Count == 0)
                account.IsDefault = true;

            account.LastUsed = DateTime.Now;
            _accounts.Add(account);
            SaveAccounts();
            return account;
        }
        else
        {
            // 更新现有账号信息
            existing.Username = account.Username;
            existing.UUID = account.UUID;
            existing.YggdrasilAccessToken = account.YggdrasilAccessToken;
            existing.YggdrasilClientToken = account.YggdrasilClientToken;
            existing.LastUsed = DateTime.Now;
            
            SaveAccounts();
            return existing;
        }
    }

    private List<GameAccount> LoadAccounts()
    {
        try
        {
            if (File.Exists(_accountsFilePath))
            {
                var json = File.ReadAllText(_accountsFilePath);
                var accounts = JsonSerializer.Deserialize<List<GameAccount>>(json);
                return accounts ?? [];
            }
        }
        catch
        {
        }

        return [];
    }

    private void SaveAccounts()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_accounts, options);
            File.WriteAllText(_accountsFilePath, json);
        }
        catch
        {
        }
    }
}
