using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// Yggdrasil 外置登录服务器配置管理服务
    /// </summary>
    public class YggdrasilServerService
    {
        private static readonly Lazy<YggdrasilServerService> _instance = new(() => new YggdrasilServerService());
        public static YggdrasilServerService Instance => _instance.Value;

        private string _serversFilePath = string.Empty;
        private List<YggdrasilServer> _servers = new List<YggdrasilServer>();

        private YggdrasilServerService()
        {
            InitializeServers();
        }

        /// <summary>
        /// 初始化服务器列表
        /// </summary>
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

            // 如果没有服务器，添加内置的 LittleSkin 服务器
            if (_servers.Count == 0)
            {
                AddBuiltInServers();
            }
        }

        /// <summary>
        /// 添加内置服务器
        /// </summary>
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

        /// <summary>
        /// 获取所有服务器
        /// </summary>
        public List<YggdrasilServer> GetAllServers()
        {
            return _servers.OrderByDescending(s => s.IsBuiltIn)
                          .ThenByDescending(s => s.LastUsed)
                          .ToList();
        }

        /// <summary>
        /// 根据 ID 获取服务器
        /// </summary>
        public YggdrasilServer? GetServerById(string serverId)
        {
            return _servers.FirstOrDefault(s => s.Id == serverId);
        }

        /// <summary>
        /// 添加自定义服务器
        /// </summary>
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

            // 检查名称是否已存在
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

            // 验证服务器配置
            if (!server.IsValid())
            {
                throw new InvalidOperationException("服务器配置无效，请检查地址格式");
            }

            _servers.Add(server);
            SaveServers();

            return server;
        }

        /// <summary>
        /// 更新服务器配置
        /// </summary>
        public void UpdateServer(string serverId, string name, string apiUrl)
        {
            var server = _servers.FirstOrDefault(s => s.Id == serverId);
            if (server == null)
            {
                throw new InvalidOperationException("服务器不存在");
            }

            if (server.IsBuiltIn)
            {
                throw new InvalidOperationException("不能修改内置服务器");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("服务器名称不能为空", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                throw new ArgumentException("服务器地址不能为空", nameof(apiUrl));
            }

            // 检查名称是否与其他服务器冲突
            if (_servers.Any(s => s.Id != serverId && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"服务器名称 '{name}' 已存在");
            }

            server.Name = name;
            server.ApiUrl = apiUrl;

            // 验证服务器配置
            if (!server.IsValid())
            {
                throw new InvalidOperationException("服务器配置无效，请检查地址格式");
            }

            SaveServers();
        }

        /// <summary>
        /// 删除服务器
        /// </summary>
        public void DeleteServer(string serverId)
        {
            var server = _servers.FirstOrDefault(s => s.Id == serverId);
            if (server == null)
            {
                return;
            }

            if (server.IsBuiltIn)
            {
                throw new InvalidOperationException("不能删除内置服务器");
            }

            _servers.Remove(server);
            SaveServers();
        }

        /// <summary>
        /// 更新服务器最后使用时间
        /// </summary>
        public void UpdateLastUsed(string serverId)
        {
            var server = _servers.FirstOrDefault(s => s.Id == serverId);
            if (server != null)
            {
                server.LastUsed = DateTime.Now;
                SaveServers();
            }
        }

        /// <summary>
        /// 加载服务器列表
        /// </summary>
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
                System.Diagnostics.Debug.WriteLine($"加载 Yggdrasil 服务器列表失败: {ex.Message}");
            }

            return new List<YggdrasilServer>();
        }

        /// <summary>
        /// 保存服务器列表
        /// </summary>
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
                System.Diagnostics.Debug.WriteLine($"保存 Yggdrasil 服务器列表失败: {ex.Message}");
            }
        }
    }
}
