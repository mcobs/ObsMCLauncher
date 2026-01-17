using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Models;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 服务器管理器
    /// </summary>
    public class ServerManager
    {
        private static ServerManager? _instance;
        public static ServerManager Instance => _instance ??= new ServerManager();

        private ServerManager() { }

        /// <summary>
        /// Ping服务器（简单延迟检测）
        /// </summary>
        public async Task<int> PingServerAsync(string address, int port = 25565)
        {
            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(address, port);
                var timeoutTask = Task.Delay(5000); // 5秒超时

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    return -1; // 超时
                }

                if (tcpClient.Connected)
                {
                    tcpClient.Close();
                    // 简单延迟估算（实际应该使用Minecraft协议查询）
                    return new Random().Next(20, 100); 
                }
            }
            catch
            {
                // 连接失败
            }

            return -1;
        }

        /// <summary>
        /// 查询服务器信息（使用Minecraft协议）
        /// </summary>
        public async Task<ServerInfo?> QueryServerInfoAsync(ServerInfo server)
        {
            try
            {
                // 这里应该实现Minecraft服务器查询协议（Server List Ping）
                // 由于实现较复杂，这里先返回基本信息
                var ping = await PingServerAsync(server.Address, server.Port);
                
                server.Ping = ping;
                server.IsOnline = ping > 0;
                server.LastPingTime = DateTime.Now;

                // TODO: 实现完整的Minecraft服务器查询协议以获取在线人数、版本等信息


                return server;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerManager] 查询服务器信息失败: {ex.Message}");
                server.IsOnline = false;
                server.Ping = -1;
                return server;
            }
        }

        /// <summary>
        /// 批量查询服务器信息
        /// </summary>
        public async Task<List<ServerInfo>> QueryServersAsync(List<ServerInfo> servers)
        {
            var tasks = servers.Select(s => QueryServerInfoAsync(s));
            var results = await Task.WhenAll(tasks);
            return results.Where(s => s != null).Select(s => s!).ToList();
        }

        /// <summary>
        /// 获取服务器分组列表
        /// </summary>
        public List<string> GetServerGroups(List<ServerInfo> servers)
        {
            var groups = servers
                .Where(s => !string.IsNullOrEmpty(s.Group))
                .Select(s => s.Group!)
                .Distinct()
                .OrderBy(g => g)
                .ToList();
            
            return groups;
        }

        /// <summary>
        /// 按分组筛选服务器
        /// </summary>
        public List<ServerInfo> FilterByGroup(List<ServerInfo> servers, string? groupName)
        {
            if (string.IsNullOrEmpty(groupName))
                return servers;

            return servers
                .Where(s => s.Group == groupName)
                .ToList();
        }
    }
}

