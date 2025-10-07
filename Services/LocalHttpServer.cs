using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ObsMCLauncher.Services
{
    /// <summary>
    /// 本地HTTP服务器，用于接收OAuth回调
    /// </summary>
    public class LocalHttpServer
    {
        private HttpListener? _listener;
        private TaskCompletionSource<string>? _authCodeReceived;
        private readonly int _port;

        public LocalHttpServer(int port = 35565)
        {
            _port = port;
        }

    /// <summary>
    /// 启动服务器并等待授权码
    /// </summary>
    /// <returns>授权码</returns>
    public async Task<string> WaitForAuthCodeAsync(CancellationToken cancellationToken = default)
    {
        _authCodeReceived = new TaskCompletionSource<string>();

        try
        {
            // 创建HTTP监听器
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();

            Debug.WriteLine($"本地服务器已启动，监听端口: {_port}");
            Console.WriteLine($"本地服务器已启动，监听 http://localhost:{_port}/");

            // 等待请求（支持取消）
            HttpListenerContext context;
            using (cancellationToken.Register(() => _listener?.Stop()))
            {
                context = await _listener.GetContextAsync();
            }
            var request = context.Request;
            var response = context.Response;

            Debug.WriteLine($"收到请求: {request.Url}");

            // 解析授权码
            var query = request.Url?.Query;
            string? code = null;
            string? error = null;

            if (!string.IsNullOrEmpty(query))
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(query);
                code = queryParams["code"];
                error = queryParams["error"];
            }

            // 返回HTML页面给用户
            string responseHtml;
            if (!string.IsNullOrEmpty(code))
            {
                responseHtml = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>登录成功</title>
    <style>
        body {
            font-family: 'Segoe UI', Arial, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }
        .container {
            background: white;
            padding: 40px;
            border-radius: 10px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
            text-align: center;
            max-width: 400px;
        }
        .success-icon {
            font-size: 64px;
            color: #4caf50;
            margin-bottom: 20px;
        }
        h1 {
            color: #333;
            margin-bottom: 10px;
        }
        p {
            color: #666;
            margin-bottom: 20px;
        }
        .info {
            background: #f5f5f5;
            padding: 15px;
            border-radius: 5px;
            color: #333;
            font-size: 14px;
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='success-icon'>✓</div>
        <h1>登录成功！</h1>
        <p>您已成功登录微软账号</p>
        <div class='info'>
            <p>您现在可以关闭此页面，返回启动器继续操作。</p>
        </div>
    </div>
</body>
</html>";

                Debug.WriteLine($"✅ 成功获取授权码: {code.Substring(0, 10)}...");
                Console.WriteLine($"✅ 微软账号授权成功！");
                _authCodeReceived.SetResult(code);
            }
            else
            {
                responseHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>登录失败</title>
    <style>
        body {{
            font-family: 'Segoe UI', Arial, sans-serif;
            background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%);
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }}
        .container {{
            background: white;
            padding: 40px;
            border-radius: 10px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
            text-align: center;
            max-width: 400px;
        }}
        .error-icon {{
            font-size: 64px;
            color: #f44336;
            margin-bottom: 20px;
        }}
        h1 {{
            color: #333;
            margin-bottom: 10px;
        }}
        p {{
            color: #666;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='error-icon'>✗</div>
        <h1>登录失败</h1>
        <p>错误: {error ?? "未知错误"}</p>
        <p>请关闭此页面并重试。</p>
    </div>
</body>
</html>";

                Debug.WriteLine($"❌ 授权失败: {error}");
                Console.WriteLine($"❌ 微软账号授权失败: {error}");
                _authCodeReceived.SetException(new Exception($"授权失败: {error}"));
            }

            // 发送响应
            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();

            // 返回授权码
            return await _authCodeReceived.Task;
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 995) // 操作已中止
        {
            Debug.WriteLine("❌ 本地服务器已被取消");
            Console.WriteLine("❌ 本地服务器已被取消");
            throw new OperationCanceledException("用户取消登录", ex);
        }
        catch (ObjectDisposedException)
        {
            Debug.WriteLine("❌ 本地服务器已被释放（可能是取消操作）");
            Console.WriteLine("❌ 本地服务器已被释放");
            throw new OperationCanceledException("用户取消登录");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"❌ 本地服务器错误: {ex.Message}");
            Console.WriteLine($"❌ 本地服务器错误: {ex.Message}");
            throw;
        }
        finally
        {
            Stop();
        }
    }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            try
            {
                if (_listener != null && _listener.IsListening)
                {
                    _listener.Stop();
                    _listener.Close();
                    Debug.WriteLine("本地服务器已停止");
                    Console.WriteLine("本地服务器已停止");
                }
            }
            catch (ObjectDisposedException)
            {
                // 已经被释放，忽略
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止服务器时出错: {ex.Message}");
            }
        }
    }
}
