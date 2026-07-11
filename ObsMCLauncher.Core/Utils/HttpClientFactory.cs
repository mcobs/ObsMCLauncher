using System;
using System.Net.Http;
using ObsMCLauncher.Core.Models;

namespace ObsMCLauncher.Core.Utils;

public static class HttpClientFactory
{
    public static HttpClientHandler CreateHandler(
        bool? skipSslValidation = null,
        System.Net.DecompressionMethods? automaticDecompression = null,
        int? maxConnectionsPerServer = null,
        bool? allowAutoRedirect = null,
        int? maxAutomaticRedirections = null,
        System.Security.Authentication.SslProtocols? sslProtocols = null)
    {
        var config = LauncherConfig.Load();
        var skipSsl = skipSslValidation ?? config.SkipSslValidation;

        var handler = new HttpClientHandler();

        if (skipSsl)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        if (automaticDecompression.HasValue)
        {
            handler.AutomaticDecompression = automaticDecompression.Value;
        }

        if (maxConnectionsPerServer.HasValue)
        {
            handler.MaxConnectionsPerServer = maxConnectionsPerServer.Value;
        }

        if (allowAutoRedirect.HasValue)
        {
            handler.AllowAutoRedirect = allowAutoRedirect.Value;
        }

        if (maxAutomaticRedirections.HasValue)
        {
            handler.MaxAutomaticRedirections = maxAutomaticRedirections.Value;
        }

        if (sslProtocols.HasValue)
        {
            handler.SslProtocols = sslProtocols.Value;
        }

        return handler;
    }

    public static HttpClient CreateClient(
        TimeSpan? timeout = null,
        bool? skipSslValidation = null,
        System.Net.DecompressionMethods? automaticDecompression = null,
        int? maxConnectionsPerServer = null,
        bool? allowAutoRedirect = null,
        int? maxAutomaticRedirections = null,
        System.Security.Authentication.SslProtocols? sslProtocols = null)
    {
        var handler = CreateHandler(skipSslValidation, automaticDecompression,
            maxConnectionsPerServer, allowAutoRedirect, maxAutomaticRedirections, sslProtocols);

        var client = new HttpClient(handler);

        if (timeout.HasValue)
        {
            client.Timeout = timeout.Value;
        }

        return client;
    }
}
