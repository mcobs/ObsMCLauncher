using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

/// <summary>
/// Yggdrasil 外置登录服务器配置管理服务
/// </summary>
public class YggdrasilServerService
{
    private static readonly Lazy<YggdrasilServerService> _instance = new(() => new YggdrasilServerService());
    public static YggdrasilServerService Instance => _instance.Value;

    private string _serversFilePath = string.Empty;
    private List<YggdrasilServer> _servers = new();

    private YggdrasilServerService()
    {
        InitializeServers();
    }

    private void InitializeServers()
    {
        var config = LauncherConfig.Load();
        _serversFilePath = Path.Combine(config.GetDataDirectory(), "yggdrasil_servers.json");

        var directory = Path.GetDirectoryName(_serversFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _servers = LoadServers();

        if (_servers.Count == 0)
        {
            AddBuiltInServers();
        }
    }

    private void AddBuiltInServers()
    {
        var littleSkin = new YggdrasilServer
        {
            Id = "littleskin",
            Name = "LittleSkin",
            ApiUrl = "littleskin.cn",
            IsBuiltIn = true,
            CreatedAt = DateTime.Now,
            LastUsed = DateTime.Now
        };

        _servers.Add(littleSkin);
        SaveServers();
    }

    public List<YggdrasilServer> GetAllServers()
    {
        return _servers
            .OrderByDescending(s => s.IsBuiltIn)
            .ThenByDescending(s => s.LastUsed)
            .ToList();
    }

    public YggdrasilServer? GetServerById(string serverId)
        => _servers.FirstOrDefault(s => s.Id == serverId);

    public YggdrasilServer AddServer(string name, string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("服务器名称不能为空", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new ArgumentException("服务器地址不能为空", nameof(apiUrl));
        }

        if (_servers.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"服务器名称 '{name}' 已存在");
        }

        var server = new YggdrasilServer
        {
            Name = name,
            ApiUrl = apiUrl,
            IsBuiltIn = false
        };

        if (!server.IsValid())
        {
            throw new InvalidOperationException("服务器配置无效，请检查地址格式");
        }

        _servers.Add(server);
        SaveServers();

        return server;
    }

    public void UpdateServer(string serverId, string name, string apiUrl)
    {
        var server = _servers.FirstOrDefault(s => s.Id == serverId);
        if (server == null) throw new InvalidOperationException("服务器不存在");
        if (server.IsBuiltIn) throw new InvalidOperationException("不能修改内置服务器");

        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("服务器名称不能为空", nameof(name));
        if (string.IsNullOrWhiteSpace(apiUrl)) throw new ArgumentException("服务器地址不能为空", nameof(apiUrl));

        if (_servers.Any(s => s.Id != serverId && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"服务器名称 '{name}' 已存在");
        }

        server.Name = name;
        server.ApiUrl = apiUrl;

        if (!server.IsValid())
        {
            throw new InvalidOperationException("服务器配置无效，请检查地址格式");
        }

        SaveServers();
    }

    public void DeleteServer(string serverId)
    {
        var server = _servers.FirstOrDefault(s => s.Id == serverId);
        if (server == null) return;
        if (server.IsBuiltIn) throw new InvalidOperationException("不能删除内置服务器");

        _servers.Remove(server);
        SaveServers();
    }

    public void UpdateLastUsed(string serverId)
    {
        var server = _servers.FirstOrDefault(s => s.Id == serverId);
        if (server == null) return;

        server.LastUsed = DateTime.Now;
        SaveServers();
    }

    private List<YggdrasilServer> LoadServers()
    {
        try
        {
            if (File.Exists(_serversFilePath))
            {
                var json = File.ReadAllText(_serversFilePath);
                var servers = JsonSerializer.Deserialize<List<YggdrasilServer>>(json);
                return servers ?? new List<YggdrasilServer>();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Error("YggdrasilServer", $"加载 Yggdrasil 服务器列表失败: {ex.Message}");
        }

        return new List<YggdrasilServer>();
    }

    private void SaveServers()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_servers, options);
            File.WriteAllText(_serversFilePath, json);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("YggdrasilServer", $"保存 Yggdrasil 服务器列表失败: {ex.Message}");
        }
    }
}
