using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ObsMCLauncher.Core.Services.Accounts;

public sealed class LocalHttpServer
{
    private HttpListener? _listener;
    private readonly int _port;

    public LocalHttpServer(int port = 35565)
    {
        _port = port;
    }

    public async Task<string> WaitForAuthCodeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();

            Debug.WriteLine($"[LocalHttpServer] Listening on http://localhost:{_port}/");

            HttpListenerContext context;
            using (cancellationToken.Register(() =>
                   {
                       try { _listener?.Stop(); } catch { }
                   }))
            {
                context = await _listener.GetContextAsync();
            }

            var request = context.Request;
            var response = context.Response;

            var query = request.Url?.Query ?? string.Empty;
            string? code = null;
            string? error = null;

            if (!string.IsNullOrEmpty(query))
            {
                // minimal query parsing to avoid System.Web dependency
                var q = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                foreach (var kv in q)
                {
                    var parts = kv.Split('=', 2);
                    if (parts.Length != 2) continue;
                    var key = Uri.UnescapeDataString(parts[0]);
                    var val = Uri.UnescapeDataString(parts[1]);
                    if (string.Equals(key, "code", StringComparison.OrdinalIgnoreCase)) code = val;
                    if (string.Equals(key, "error", StringComparison.OrdinalIgnoreCase)) error = val;
                }
            }

            var responseHtml = !string.IsNullOrEmpty(code)
                ? "<!DOCTYPE html><html><head><meta charset='utf-8'><title>登录成功</title></head><body><h2>登录成功</h2><p>你可以关闭此页面并返回启动器。</p></body></html>"
                : $"<!DOCTYPE html><html><head><meta charset='utf-8'><title>登录失败</title></head><body><h2>登录失败</h2><p>错误: {error ?? "未知错误"}</p></body></html>";

            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            response.Close();

            if (!string.IsNullOrEmpty(code))
                return code;

            throw new Exception($"授权失败: {error}");
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 995)
        {
            throw new OperationCanceledException("用户取消登录", ex);
        }
        catch (ObjectDisposedException)
        {
            throw new OperationCanceledException("用户取消登录");
        }
        finally
        {
            Stop();
        }
    }

    public void Stop()
    {
        try
        {
            if (_listener != null)
            {
                if (_listener.IsListening) _listener.Stop();
                _listener.Close();
            }
        }
        catch
        {
        }
        finally
        {
            _listener = null;
        }
    }
}
