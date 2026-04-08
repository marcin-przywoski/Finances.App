using System.Net;
using System.Net.Sockets;

namespace Finances.App.PlaywrightTests.Infrastructure;

internal sealed class StaticSiteServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly HttpListener _listener = new();
    private string _siteRootPath;
    private Task? _listenerTask;

    public StaticSiteServer(string siteRootPath)
    {
        _siteRootPath = siteRootPath;
        BaseUri = new Uri($"http://127.0.0.1:{GetAvailablePort()}/");
        _listener.Prefixes.Add(BaseUri.ToString());
    }

    public Uri BaseUri { get; }

    public void SwitchSiteRoot(string siteRootPath)
    {
        _siteRootPath = siteRootPath;
    }

    public void Start()
    {
        _listener.Start();
        _listenerTask = Task.Run(ListenAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();

        if (_listener.IsListening)
        {
            _listener.Stop();
            _listener.Close();
        }

        if (_listenerTask is not null)
        {
            await _listenerTask;
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            HttpListenerContext? context = null;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (_cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(() => HandleRequestAsync(context), _cancellationTokenSource.Token);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var siteRootPath = _siteRootPath;
            var requestedPath = context.Request.Url?.AbsolutePath ?? "/";
            var relativePath = Uri.UnescapeDataString(requestedPath.TrimStart('/'));

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = "index.html";
            }

            var candidatePath = Path.Combine(siteRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(candidatePath))
            {
                candidatePath = Path.Combine(candidatePath, "index.html");
            }

            if (!File.Exists(candidatePath) && !Path.HasExtension(relativePath))
            {
                candidatePath = Path.Combine(siteRootPath, "index.html");
            }

            if (!File.Exists(candidatePath))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            context.Response.ContentType = GetContentType(candidatePath);
            context.Response.Headers[HttpResponseHeader.CacheControl] = "no-cache, no-store";

            await using var fileStream = File.OpenRead(candidatePath);
            context.Response.ContentLength64 = fileStream.Length;
            await fileStream.CopyToAsync(context.Response.OutputStream, _cancellationTokenSource.Token);
            context.Response.OutputStream.Close();
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException) when (_cancellationTokenSource.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css",
            ".dll" => "application/octet-stream",
            ".html" => "text/html; charset=utf-8",
            ".ico" => "image/x-icon",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".wasm" => "application/wasm",
            ".webmanifest" => "application/manifest+json; charset=utf-8",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }
}