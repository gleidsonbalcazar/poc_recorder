using System.Net;
using System.Text;

namespace Agent;

/// <summary>
/// Simple HTTP server to serve media files for preview in dashboard
/// </summary>
public class MediaHttpServer : IDisposable
{
    private HttpListener _listener;
    private readonly string _mediaBasePath;
    private readonly int _port;
    private bool _isRunning;
    private Task? _serverTask;
    private CancellationTokenSource? _cts;
    private readonly FFmpegRecorder? _videoRecorder;

    // Allowed file extensions for security
    private readonly HashSet<string> _allowedExtensions = new()
    {
        ".mp4", ".avi", ".mkv"
    };

    public bool IsRunning => _isRunning;
    public int Port => _port;

    public MediaHttpServer(string mediaBasePath, int port = 9000)
    {
        _mediaBasePath = mediaBasePath;
        _port = port;
        _listener = new HttpListener();
    }

    public MediaHttpServer(string mediaBasePath, int port, FFmpegRecorder? videoRecorder)
        : this(mediaBasePath, port)
    {
        _videoRecorder = videoRecorder;
    }

    /// <summary>
    /// Start the HTTP server
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            Console.WriteLine("[MediaHttpServer] Server is already running");
            return;
        }

        try
        {
            // Try to listen on all interfaces first
            string prefix = $"http://+:{_port}/";
            _listener.Prefixes.Add(prefix);

            try
            {
                _listener.Start();
                _isRunning = true;

                Console.WriteLine($"[MediaHttpServer] Started on port {_port} (all interfaces)");
                Console.WriteLine($"[MediaHttpServer] Serving files from: {_mediaBasePath}");

                // Start processing requests in background
                _cts = new CancellationTokenSource();
                _serverTask = Task.Run(() => ProcessRequests(_cts.Token));
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access Denied
            {
                // Fallback to localhost only (doesn't require admin)
                Console.WriteLine("[MediaHttpServer] Access denied on all interfaces. Falling back to localhost only...");

                _listener.Close();
                _listener = new HttpListener();

                string localhostPrefix = $"http://localhost:{_port}/";
                _listener.Prefixes.Add(localhostPrefix);
                _listener.Start();
                _isRunning = true;

                Console.WriteLine($"[MediaHttpServer] Started on port {_port} (localhost only)");
                Console.WriteLine($"[MediaHttpServer] Serving files from: {_mediaBasePath}");
                Console.WriteLine("[MediaHttpServer] Note: Preview only works on same machine (localhost)");

                // Start processing requests in background
                _cts = new CancellationTokenSource();
                _serverTask = Task.Run(() => ProcessRequests(_cts.Token));
            }
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"[MediaHttpServer] Failed to start: {ex.Message}");

            if (ex.ErrorCode == 5)
            {
                Console.WriteLine("[MediaHttpServer] Run as Administrator for remote access, or use netsh:");
                Console.WriteLine($"  netsh http add urlacl url=http://+:{_port}/ user=Everyone");
            }

            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaHttpServer] Error starting server: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Stop the HTTP server
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }

        Console.WriteLine("[MediaHttpServer] Stopping server...");

        _cts?.Cancel();
        _listener.Stop();
        _isRunning = false;

        if (_serverTask != null)
        {
            _serverTask.Wait(TimeSpan.FromSeconds(5));
        }

        Console.WriteLine("[MediaHttpServer] Server stopped");
    }

    /// <summary>
    /// Process incoming HTTP requests
    /// </summary>
    private async Task ProcessRequests(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (HttpListenerException)
            {
                // Server stopped
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaHttpServer] Error getting context: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handle individual HTTP request
    /// </summary>
    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            Console.WriteLine($"[MediaHttpServer] {request.HttpMethod} {request.Url?.PathAndQuery}");

            // Enable CORS for dashboard
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Range");
            response.AddHeader("Access-Control-Expose-Headers", "Content-Range, Content-Length, Accept-Ranges");

            // Handle OPTIONS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            // Only allow GET
            if (request.HttpMethod != "GET")
            {
                SendError(response, 405, "Method Not Allowed");
                return;
            }

            // Parse request path
            string? path = request.Url?.AbsolutePath;
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                SendResponse(response, "Media HTTP Server - Ready");
                return;
            }

            // Extract filename from path (/media/filename.mp4)
            if (!path.StartsWith("/media/"))
            {
                SendError(response, 404, "Not Found");
                return;
            }

            string filename = path.Substring(7); // Remove "/media/"
            filename = System.Net.WebUtility.UrlDecode(filename) ?? filename;

            // Serve file
            ServeFile(response, filename, request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaHttpServer] Error handling request: {ex.Message}");
            try
            {
                SendError(response, 500, "Internal Server Error");
            }
            catch { }
        }
    }

    /// <summary>
    /// Serve a media file
    /// </summary>
    private void ServeFile(HttpListenerResponse response, string filename, HttpListenerRequest request)
    {
        try
        {
            // Security: validate filename
            string extension = Path.GetExtension(filename).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
            {
                SendError(response, 403, "Forbidden: File type not allowed");
                return;
            }

            // Security: prevent path traversal
            if (filename.Contains("..") || filename.Contains("\\") || filename.Contains("//"))
            {
                SendError(response, 403, "Forbidden: Invalid path");
                return;
            }

            // Find file in media directories
            string? filePath = FindMediaFile(filename);

            if (filePath == null || !File.Exists(filePath))
            {
                SendError(response, 404, $"File not found: {filename}");
                return;
            }

            // Check if file is currently being recorded
            if (IsFileBeingRecorded(filePath))
            {
                SendError(response, 423, "File is currently being recorded. Please wait until recording completes.");
                return;
            }

            // Get file info
            var fileInfo = new FileInfo(filePath);
            long fileLength = fileInfo.Length;

            // Set content type
            response.ContentType = GetContentType(extension);
            response.AddHeader("Accept-Ranges", "bytes");

            // Handle range requests (for video seeking)
            string? rangeHeader = request.Headers["Range"];
            if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
            {
                ServeFileRange(response, filePath, fileLength, rangeHeader);
            }
            else
            {
                // Serve entire file
                response.ContentLength64 = fileLength;
                response.StatusCode = 200;

                using var fileStream = File.OpenRead(filePath);
                fileStream.CopyTo(response.OutputStream);
            }

            response.Close();
            Console.WriteLine($"[MediaHttpServer] Served: {filename} ({fileLength} bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaHttpServer] Error serving file: {ex.Message}");
            SendError(response, 500, "Error serving file");
        }
    }

    /// <summary>
    /// Serve partial file content (range request)
    /// </summary>
    private void ServeFileRange(HttpListenerResponse response, string filePath, long fileLength, string rangeHeader)
    {
        try
        {
            // Parse range header: "bytes=0-1023"
            string range = rangeHeader.Substring(6); // Remove "bytes="
            string[] parts = range.Split('-');

            long start = long.Parse(parts[0]);
            long end = parts.Length > 1 && !string.IsNullOrEmpty(parts[1])
                ? long.Parse(parts[1])
                : fileLength - 1;

            // Validate range
            if (start > end || start < 0 || end >= fileLength)
            {
                response.StatusCode = 416; // Range Not Satisfiable
                response.AddHeader("Content-Range", $"bytes */{fileLength}");
                response.Close();
                return;
            }

            long contentLength = end - start + 1;

            // Set response headers
            response.StatusCode = 206; // Partial Content
            response.ContentLength64 = contentLength;
            response.AddHeader("Content-Range", $"bytes {start}-{end}/{fileLength}");

            // Stream partial content
            using var fileStream = File.OpenRead(filePath);
            fileStream.Seek(start, SeekOrigin.Begin);

            byte[] buffer = new byte[8192];
            long remaining = contentLength;

            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = fileStream.Read(buffer, 0, toRead);

                if (read == 0) break;

                response.OutputStream.Write(buffer, 0, read);
                remaining -= read;
            }

            response.Close();
            Console.WriteLine($"[MediaHttpServer] Served range: {start}-{end}/{fileLength}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MediaHttpServer] Error serving range: {ex.Message}");
        }
    }

    /// <summary>
    /// Find media file in video directories
    /// </summary>
    private string? FindMediaFile(string filename)
    {
        // Search in videos directory
        string videosDir = Path.Combine(_mediaBasePath, "videos");

        if (Directory.Exists(videosDir))
        {
            // Search recursively
            var files = Directory.GetFiles(videosDir, filename, SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return files[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Check if a file is currently being recorded
    /// </summary>
    private bool IsFileBeingRecorded(string filePath)
    {
        // Normalize paths for comparison
        string normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();

        // Check video recorder
        if (_videoRecorder != null && _videoRecorder.CurrentRecordingPath != null)
        {
            string videoPath = Path.GetFullPath(_videoRecorder.CurrentRecordingPath).ToLowerInvariant();

            // Se estiver em modo segmentação (SegmentSeconds > 0), CurrentRecordingPath é um diretório
            if (_videoRecorder.SegmentSeconds > 0)
            {
                // Verificar se arquivo requisitado está no diretório de gravação
                if (normalizedPath.StartsWith(videoPath))
                {
                    // Com segmentação, apenas o segmento MAIS RECENTE pode estar sendo escrito
                    // Segmentos anteriores já estão finalizados e podem ser servidos
                    // Verificar se arquivo foi modificado nos últimos 60 segundos
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Exists)
                        {
                            var age = DateTime.Now - fileInfo.LastWriteTime;
                            if (age.TotalSeconds < 60)
                            {
                                // Arquivo muito recente, pode estar sendo escrito
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // Se não conseguiu verificar, assumir que está seguro
                        return false;
                    }
                }
            }
            else
            {
                // Modo arquivo único: comparação direta
                if (normalizedPath == videoPath)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Get MIME content type for file extension
    /// </summary>
    private string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Send simple text response
    /// </summary>
    private void SendResponse(HttpListenerResponse response, string message)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        response.ContentType = "text/plain";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = 200;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    /// <summary>
    /// Send error response
    /// </summary>
    private void SendError(HttpListenerResponse response, int statusCode, string message)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            response.ContentType = "text/plain";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = statusCode;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _listener.Close();
    }
}
