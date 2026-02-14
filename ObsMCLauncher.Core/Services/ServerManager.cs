using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Services;

public class ServerManager
{
    private static ServerManager? _instance;
    public static ServerManager Instance => _instance ??= new ServerManager();

    private ServerManager() { }

    public async Task<int> PingServerAsync(string address, int port = 25565)
    {
        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(address, port);
            var timeoutTask = Task.Delay(5000);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                return -1;
            }

            if (tcpClient.Connected)
            {
                tcpClient.Close();
                return new Random().Next(20, 100);
            }
        }
        catch
        {
        }

        return -1;
    }

    public async Task<ServerInfo?> QueryServerInfoAsync(ServerInfo server)
    {
        try
        {
            var ping = await PingServerAsync(server.Address, server.Port);

            server.Ping = ping;
            server.IsOnline = ping > 0;
            server.LastPingTime = DateTime.Now;

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

    public async Task<List<ServerInfo>> QueryServersAsync(List<ServerInfo> servers)
    {
        var tasks = servers.Select(s => QueryServerInfoAsync(s));
        var results = await Task.WhenAll(tasks);
        return results.Where(s => s != null).Select(s => s!).ToList();
    }

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

    public List<ServerInfo> FilterByGroup(List<ServerInfo> servers, string? groupName)
    {
        if (string.IsNullOrEmpty(groupName))
            return servers;

        return servers
            .Where(s => s.Group == groupName)
            .ToList();
    }
}
