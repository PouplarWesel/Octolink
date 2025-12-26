using System.IO;
using System.Net;
using System.Text;

namespace VirtualControllerServer
{
    /// <summary>
    /// Simple HTTP server to serve the web controller interface
    /// </summary>
    public class HttpServer
    {
        private readonly int _port;
        private readonly string _localIp;
        private HttpListener? _listener;
        private bool _running;
        private readonly Dictionary<string, string> _mimeTypes = new()
        {
            { ".html", "text/html" },
            { ".css", "text/css" },
            { ".js", "application/javascript" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".svg", "image/svg+xml" },
            { ".ico", "image/x-icon" },
            { ".json", "application/json" }
        };

        public HttpServer(int port, string localIp)
        {
            _port = port;
            _localIp = localIp;
        }

        public async Task StartAsync()
        {
            _listener = new HttpListener();
            
            try
            {
                // Try binding to all interfaces (works after running Setup.bat as admin once)
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // Fallback: try specific IP (may work on some systems)
                _listener = new HttpListener();
                try
                {
                    _listener.Prefixes.Add($"http://{_localIp}:{_port}/");
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Start();
                }
                catch (HttpListenerException)
                {
                    // Last resort: localhost only
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    _listener.Start();
                    throw new Exception($"Server started on localhost only. Run Setup.bat as Administrator to allow phone connections.");
                }
            }

            _running = true;
            _ = Task.Run(AcceptRequestsAsync);
        }

        private async Task AcceptRequestsAsync()
        {
            while (_running && _listener != null)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context));
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string path = request.Url?.LocalPath ?? "/";
                if (path == "/") path = "/index.html";

                // Serve embedded or file-based resources
                var content = await GetResourceAsync(path);
                
                if (content != null)
                {
                    string ext = Path.GetExtension(path).ToLower();
                    response.ContentType = _mimeTypes.GetValueOrDefault(ext, "application/octet-stream");
                    response.StatusCode = 200;
                    await response.OutputStream.WriteAsync(content);
                }
                else
                {
                    response.StatusCode = 404;
                    var notFound = Encoding.UTF8.GetBytes("404 Not Found");
                    await response.OutputStream.WriteAsync(notFound);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTTP error: {ex.Message}");
                response.StatusCode = 500;
            }
            finally
            {
                response.Close();
            }
        }

        private async Task<byte[]?> GetResourceAsync(string path)
        {
            // First try wwwroot folder next to executable
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(basePath, "wwwroot", path.TrimStart('/'));

            if (File.Exists(filePath))
            {
                return await File.ReadAllBytesAsync(filePath);
            }

            // Try development path
            string devPath = Path.Combine(basePath, "..", "..", "..", "wwwroot", path.TrimStart('/'));
            if (File.Exists(devPath))
            {
                return await File.ReadAllBytesAsync(devPath);
            }

            return null;
        }

        public void Stop()
        {
            _running = false;
            try
            {
                _listener?.Stop();
            }
            catch { }
            try
            {
                _listener?.Close();
            }
            catch { }
            _listener = null;
        }
    }
}
