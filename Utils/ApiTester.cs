using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ObsMCLauncher.Utils
{
    /// <summary>
    /// API 测试工具
    /// </summary>
    public static class ApiTester
    {
        private static readonly HttpClient _httpClient;

        static ApiTester()
        {
            // 创建支持自动解压缩的 HttpClient
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ObsMCLauncher/1.0");
        }

        /// <summary>
        /// 测试 API 端点
        /// </summary>
        public static async Task<(bool Success, string Message, string Response)> TestApiAsync(string url)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"HTTP {response.StatusCode}: {response.ReasonPhrase}", "");
                }

                var content = await response.Content.ReadAsStringAsync();
                
                // 尝试解析为 JSON 以验证
                try
                {
                    var jsonDoc = JsonDocument.Parse(content);
                    var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                    return (true, $"成功！响应长度: {content.Length} 字节", preview);
                }
                catch
                {
                    return (false, "响应不是有效的 JSON", content.Substring(0, Math.Min(200, content.Length)));
                }
            }
            catch (TaskCanceledException)
            {
                return (false, "请求超时（10秒）", "");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"网络错误: {ex.Message}", "");
            }
            catch (Exception ex)
            {
                return (false, $"异常: {ex.Message}", "");
            }
        }
    }
}

