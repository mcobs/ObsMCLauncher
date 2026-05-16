using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ObsMCLauncher.Core.Models;
using ObsMCLauncher.Core.Utils;

namespace ObsMCLauncher.Core.Services;

public class ServerManager
{
    private static ServerManager? _instance;
    public static ServerManager Instance => _instance ??= new ServerManager();

    private const int PingTimeoutMs = 4000;
    private const int MaxRetries = 1;

    private ServerManager() { }

    public async Task<int> PingServerAsync(string address, int port = 25565)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            DebugLogger.Info("ServerManager", $"开始 Ping 服务器: {address}:{port}");
            
            using var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(PingTimeoutMs);
            
            await tcpClient.ConnectAsync(address, port, cts.Token);
            DebugLogger.Info("ServerManager", $"TCP 连接成功: {address}:{port}");

            using var stream = tcpClient.GetStream();
            stream.WriteTimeout = PingTimeoutMs;
            stream.ReadTimeout = PingTimeoutMs;

            var handshake = BuildHandshakePacket(address, (ushort)port);
            await stream.WriteAsync(handshake, 0, handshake.Length);
            await stream.WriteAsync(new byte[] { 0x01, 0x00 });
            await stream.FlushAsync();
            DebugLogger.Info("ServerManager", $"已发送握手包和状态请求包，包长度: {handshake.Length + 2}");

            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf, 0, buf.Length);
            DebugLogger.Info("ServerManager", $"收到响应数据: {read} 字节");

            tcpClient.Close();
            sw.Stop();
            var ping = (int)sw.ElapsedMilliseconds;
            DebugLogger.Info("ServerManager", $"Ping 完成: {address}:{port} = {ping}ms");
            return ping;
        }
        catch (SocketException ex)
        {
            sw.Stop();
            DebugLogger.Warn("ServerManager", $"Ping 失败 (Socket): {address}:{port} - {ex.SocketErrorCode}: {ex.Message}");
            return -1;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            DebugLogger.Warn("ServerManager", $"Ping 超时: {address}:{port} (超时时间: {PingTimeoutMs}ms)");
            return -1;
        }
        catch (Exception ex)
        {
            sw.Stop();
            DebugLogger.Error("ServerManager", $"Ping 异常: {address}:{port} - {ex.GetType().Name}: {ex.Message}");
            return -1;
        }
    }

    public async Task<(int Ping, string? Version, int OnlinePlayers, int MaxPlayers, string? Motd, string? Icon)> QueryServerInfoAsync(string address, int port = 25565)
    {
        var sw = Stopwatch.StartNew();
        DebugLogger.Info("ServerManager", $"开始查询服务器信息: {address}:{port}");
        
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                DebugLogger.Info("ServerManager", $"第 {attempt + 1} 次尝试连接 {address}:{port}");
                
                using var tcpClient = new TcpClient();
                using var cts = new CancellationTokenSource(PingTimeoutMs);
                
                await tcpClient.ConnectAsync(address, port, cts.Token);
                DebugLogger.Info("ServerManager", $"TCP 连接成功: {address}:{port}");

                using var stream = tcpClient.GetStream();
                stream.WriteTimeout = PingTimeoutMs;
                stream.ReadTimeout = PingTimeoutMs;

                var handshake = BuildHandshakePacket(address, (ushort)port);
                await stream.WriteAsync(handshake, 0, handshake.Length);
                await stream.WriteAsync(new byte[] { 0x01, 0x00 });
                await stream.FlushAsync();
                DebugLogger.Info("ServerManager", $"已发送握手包({handshake.Length}字节)和状态请求包(2字节)");

                var length = ReadVarInt(stream);
                if (length <= 0) 
                {
                    DebugLogger.Warn("ServerManager", $"无效的响应长度: {length}");
                    continue;
                }
                DebugLogger.Info("ServerManager", $"响应包总长度: {length}");

                var packetId = ReadVarInt(stream);
                if (packetId != 0x00) 
                {
                    DebugLogger.Warn("ServerManager", $"无效的包ID: {packetId}，期望 0x00");
                    continue;
                }
                DebugLogger.Info("ServerManager", $"包ID验证通过: 0x{packetId:X2}");

                var jsonLength = ReadVarInt(stream);
                if (jsonLength <= 0 || jsonLength > 65536) 
                {
                    DebugLogger.Warn("ServerManager", $"无效的JSON长度: {jsonLength}");
                    continue;
                }
                DebugLogger.Info("ServerManager", $"JSON数据长度: {jsonLength}");

                var jsonBytes = new byte[jsonLength];
                var totalRead = 0;
                while (totalRead < jsonLength)
                {
                    var r = await stream.ReadAsync(jsonBytes, totalRead, jsonLength - totalRead);
                    if (r == 0) 
                    {
                        DebugLogger.Warn("ServerManager", $"读取JSON数据时连接中断，已读取 {totalRead}/{jsonLength}");
                        break;
                    }
                    totalRead += r;
                    DebugLogger.Info("ServerManager", $"正在读取JSON数据: {totalRead}/{jsonLength}");
                }

                tcpClient.Close();
                sw.Stop();

                var json = Encoding.UTF8.GetString(jsonBytes, 0, totalRead);
                DebugLogger.Info("ServerManager", $"收到服务器响应 JSON: {json.Substring(0, Math.Min(200, json.Length))}{(json.Length > 200 ? "..." : "")}");
                
                var result = ParseServerJson(json, (int)sw.ElapsedMilliseconds);
                DebugLogger.Info("ServerManager", $"查询完成: {address}:{port} - 延迟:{result.Ping}ms, 版本:{result.Version ?? "未知"}, 玩家:{result.OnlinePlayers}/{result.MaxPlayers}, MOTD:{(result.Motd?.Length ?? 0)}字符");
                return result;
            }
            catch (SocketException ex)
            {
                DebugLogger.Warn("ServerManager", $"查询失败(Socket): {address}:{port} - {ex.SocketErrorCode}: {ex.Message}");
            }
            catch (OperationCanceledException)
            {
                DebugLogger.Warn("ServerManager", $"查询超时: {address}:{port} (超时时间: {PingTimeoutMs}ms)");
            }
            catch (Exception ex)
            {
                DebugLogger.Error("ServerManager", $"查询异常: {address}:{port} - {ex.GetType().Name}: {ex.Message}");
            }
        }
        sw.Stop();
        DebugLogger.Warn("ServerManager", $"查询失败(所有尝试均失败): {address}:{port}");
        return (-1, null, 0, 0, null, null);
    }

    private static (int Ping, string? Version, int OnlinePlayers, int MaxPlayers, string? Motd, string? Icon) ParseServerJson(string json, int ping)
    {
        try
        {
            DebugLogger.Info("ServerManager", "开始解析服务器响应 JSON");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var version = root.TryGetProperty("version", out var ver) && ver.TryGetProperty("name", out var verName)
                ? verName.GetString() : null;
            DebugLogger.Info("ServerManager", $"解析版本信息: {(version != null ? $"成功 ({version})" : "未找到")}");

            int online = 0, max = 0;
            if (root.TryGetProperty("players", out var players))
            {
                if (players.TryGetProperty("online", out var on)) online = on.GetInt32();
                if (players.TryGetProperty("max", out var mx)) max = mx.GetInt32();
            }
            DebugLogger.Info("ServerManager", $"解析玩家信息: 在线{online}/最大{max}");

            var motd = root.TryGetProperty("description", out var desc) ? ParseMotd(desc) : null;
            DebugLogger.Info("ServerManager", $"解析MOTD: {(motd != null ? $"{motd.Length}字符" : "未找到")}");

            var icon = root.TryGetProperty("favicon", out var fav) ? fav.GetString() : null;
            DebugLogger.Info("ServerManager", $"解析图标: {(icon != null ? $"存在 ({icon.Length}字符)" : "未找到")}");

            return (ping, version, online, max, motd, icon);
        }
        catch (JsonException ex)
        {
            DebugLogger.Error("ServerManager", $"JSON解析失败: {ex.Message}");
            return (-1, null, 0, 0, null, null);
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ServerManager", $"解析服务器信息异常: {ex.GetType().Name}: {ex.Message}");
            return (-1, null, 0, 0, null, null);
        }
    }

    private static string? ParseMotd(JsonElement element)
    {
        try
        {
            DebugLogger.Info("ServerManager", $"MOTD元素类型: {element.ValueKind}");
            
            if (element.ValueKind == JsonValueKind.String)
            {
                var result = element.GetString();
                DebugLogger.Info("ServerManager", $"MOTD(字符串类型): {result?.Length ?? 0}字符");
                return result;
            }

            if (element.TryGetProperty("text", out var text))
            {
                var result = text.GetString();
                DebugLogger.Info("ServerManager", $"MOTD(对象类型-text): {result?.Length ?? 0}字符");
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }

            if (element.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
            {
                DebugLogger.Info("ServerManager", $"MOTD(复合类型): 包含{extra.GetArrayLength()}个子元素");
                var sb = new StringBuilder();
                foreach (var e in extra.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(e.GetString());
                    }
                    else if (e.TryGetProperty("text", out var t))
                    {
                        sb.Append(t.GetString());
                    }
                }
                var result = sb.ToString();
                DebugLogger.Info("ServerManager", $"MOTD(复合类型合并后): {result.Length}字符");
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }

            var rawResult = element.GetRawText();
            DebugLogger.Info("ServerManager", $"MOTD(回退到原始文本): {rawResult.Length}字符");
            return rawResult;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ServerManager", $"解析MOTD异常: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<ServerInfo?> QueryServerInfoAsync(ServerInfo server)
    {
        try
        {
            var (ping, version, online, max, motd, icon) = await QueryServerInfoAsync(server.Address, server.Port);

            server.Ping = ping;
            server.IsOnline = ping > 0;
            server.LastPingTime = DateTime.Now;
            server.Version = version;
            server.OnlinePlayers = online;
            server.MaxPlayers = max;
            server.Motd = motd;

            if (!string.IsNullOrEmpty(icon))
            {
                server.IconPath = SaveServerIcon(server.Id, icon);
            }

            return server;
        }
        catch (Exception ex)
        {
            DebugLogger.Error("ServerManager", $"查询服务器 [{server.Name}] 失败: {ex.Message}");
            server.IsOnline = false;
            server.Ping = -1;
            return server;
        }
    }

    private static string? SaveServerIcon(string serverId, string iconData)
    {
        try
        {
            const string prefix = "data:image/png;base64,";
            if (!iconData.StartsWith(prefix)) return null;

            var base64 = iconData[prefix.Length..];
            var bytes = Convert.FromBase64String(base64);

            var iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OMCL", "cache", "server_icons");
            Directory.CreateDirectory(iconsDir);

            var filePath = Path.Combine(iconsDir, $"{serverId}.png");
            File.WriteAllBytes(filePath, bytes);
            DebugLogger.Info("ServerManager", $"保存服务器图标: {filePath} ({bytes.Length}字节)");
            return filePath;
        }
        catch (Exception ex)
        {
            DebugLogger.Warn("ServerManager", $"保存服务器图标失败: {ex.Message}");
            return null;
        }
    }

    public async Task<List<ServerInfo>> QueryServersAsync(List<ServerInfo> servers)
    {
        var tasks = servers.Select(s => QueryServerInfoAsync(s));
        var results = await Task.WhenAll(tasks);
        return results.Where(s => s != null).Select(s => s!).ToList();
    }

    public bool ValidateAddress(string address, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(address))
        {
            error = "服务器地址不能为空";
            return false;
        }
        if (address.Length > 255)
        {
            error = "服务器地址过长";
            return false;
        }
        return true;
    }

    public bool ValidatePort(int port, out string error)
    {
        error = string.Empty;
        if (port < 1 || port > 65535)
        {
            error = "端口号必须在1-65535之间";
            return false;
        }
        return true;
    }

    private static byte[] BuildHandshakePacket(string address, ushort port)
    {
        var addressBytes = Encoding.UTF8.GetBytes(address);
        var packetData = new byte[1 + VarIntLength(-1) + VarIntLength(addressBytes.Length) + addressBytes.Length + 2 + 1];
        var offset = 0;
        packetData[offset++] = 0x00;
        WriteVarIntTo(packetData, ref offset, -1);
        WriteVarIntTo(packetData, ref offset, addressBytes.Length);
        Buffer.BlockCopy(addressBytes, 0, packetData, offset, addressBytes.Length);
        offset += addressBytes.Length;
        packetData[offset++] = (byte)(port >> 8);
        packetData[offset++] = (byte)(port & 0xFF);
        packetData[offset] = 0x01;

        var fullPacket = new byte[VarIntLength(packetData.Length) + packetData.Length];
        offset = 0;
        WriteVarIntTo(fullPacket, ref offset, packetData.Length);
        Buffer.BlockCopy(packetData, 0, fullPacket, offset, packetData.Length);
        return fullPacket;
    }

    private static int VarIntLength(int value)
    {
        var v = (uint)value;
        var len = 0;
        do
        {
            len++;
            v >>= 7;
        } while (v != 0);
        return len;
    }

    private static void WriteVarIntTo(byte[] buffer, ref int offset, int value)
    {
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            buffer[offset++] = b;
        } while (v != 0);
    }

    private static int ReadVarInt(NetworkStream stream)
    {
        var value = 0;
        var shift = 0;
        for (var i = 0; i < 5; i++)
        {
            var b = stream.ReadByte();
            if (b == -1) return -1;
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
        return -1;
    }
}
