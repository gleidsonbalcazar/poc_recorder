using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Agent.Workers;
using Agent.Database;

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
    private readonly ILogger? _logger;
    private readonly ConfigurationManager? _configManager;
    private readonly VideoRecorderWorker? _recorderWorker;
    private readonly UploadWorker? _uploadWorker;
    private readonly DatabaseManager? _database;
    private readonly string _webUIPassword;

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
        _webUIPassword = "admin"; // Default password
    }

    public MediaHttpServer(string mediaBasePath, int port, FFmpegRecorder? videoRecorder)
        : this(mediaBasePath, port)
    {
        _videoRecorder = videoRecorder;
    }

    public MediaHttpServer(
        string mediaBasePath,
        int port,
        FFmpegRecorder? videoRecorder,
        ILogger? logger,
        ConfigurationManager? configManager,
        VideoRecorderWorker? recorderWorker,
        UploadWorker? uploadWorker,
        DatabaseManager? database,
        string webUIPassword = "admin")
        : this(mediaBasePath, port, videoRecorder)
    {
        _logger = logger;
        _configManager = configManager;
        _recorderWorker = recorderWorker;
        _uploadWorker = uploadWorker;
        _database = database;
        _webUIPassword = webUIPassword;
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
    /// Check HTTP Basic Auth for protected endpoints
    /// </summary>
    private bool CheckAuthentication(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            string? authHeader = request.Headers["Authorization"];

            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
            {
                SendAuthRequired(response);
                return false;
            }

            // Decode Base64 credentials
            string encodedCredentials = authHeader.Substring(6);
            byte[] credentialsBytes = Convert.FromBase64String(encodedCredentials);
            string credentials = Encoding.UTF8.GetString(credentialsBytes);

            // Parse username:password
            int colonIndex = credentials.IndexOf(':');
            if (colonIndex == -1)
            {
                SendAuthRequired(response);
                return false;
            }

            string username = credentials.Substring(0, colonIndex);
            string password = credentials.Substring(colonIndex + 1);

            // Validate credentials (username can be anything, only password matters)
            if (password == _webUIPassword)
            {
                return true;
            }

            SendAuthRequired(response);
            return false;
        }
        catch
        {
            SendAuthRequired(response);
            return false;
        }
    }

    /// <summary>
    /// Send 401 Unauthorized response
    /// </summary>
    private void SendAuthRequired(HttpListenerResponse response)
    {
        response.StatusCode = 401;
        response.AddHeader("WWW-Authenticate", "Basic realm=\"Paneas Agent Configuration\"");
        byte[] buffer = Encoding.UTF8.GetBytes("Authentication required");
        response.ContentType = "text/plain";
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
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
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Range, Authorization");
            response.AddHeader("Access-Control-Expose-Headers", "Content-Range, Content-Length, Accept-Ranges");

            // Handle OPTIONS preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            // Parse request path
            string? path = request.Url?.AbsolutePath;
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                SendResponse(response, "Media HTTP Server - Ready");
                return;
            }

            // Route to appropriate handler
            if (path == "/config")
            {
                // Configuration page (requires auth)
                if (!CheckAuthentication(request, response)) return;
                if (request.HttpMethod != "GET")
                {
                    SendError(response, 405, "Method Not Allowed");
                    return;
                }
                HandleConfigPage(response);
            }
            else if (path == "/api/config")
            {
                // Configuration API (requires auth)
                if (!CheckAuthentication(request, response)) return;
                if (request.HttpMethod == "GET")
                {
                    HandleGetConfig(response);
                }
                else if (request.HttpMethod == "POST")
                {
                    HandlePostConfig(request, response);
                }
                else
                {
                    SendError(response, 405, "Method Not Allowed");
                }
            }
            else if (path == "/api/status")
            {
                // Status API (requires auth)
                if (!CheckAuthentication(request, response)) return;
                if (request.HttpMethod != "GET")
                {
                    SendError(response, 405, "Method Not Allowed");
                    return;
                }
                HandleGetStatus(response);
            }
            else if (path == "/api/restart")
            {
                // Restart API (requires auth)
                if (!CheckAuthentication(request, response)) return;
                if (request.HttpMethod != "POST")
                {
                    SendError(response, 405, "Method Not Allowed");
                    return;
                }
                HandleRestart(response);
            }
            else if (path.StartsWith("/media/"))
            {
                // Media files (no auth required)
                if (request.HttpMethod != "GET")
                {
                    SendError(response, 405, "Method Not Allowed");
                    return;
                }
                string filename = path.Substring(7); // Remove "/media/"
                filename = System.Net.WebUtility.UrlDecode(filename) ?? filename;
                ServeFile(response, filename, request);
            }
            else
            {
                SendError(response, 404, "Not Found");
            }
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
    /// Handle GET /api/config - Return current configuration
    /// </summary>
    private void HandleGetConfig(HttpListenerResponse response)
    {
        try
        {
            if (_configManager == null)
            {
                SendJsonError(response, 503, "Configuration manager not available");
                return;
            }

            var config = _configManager.Current;
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            SendJsonResponse(response, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError("[MediaHttpServer] Error getting config: {Message}", ex.Message);
            SendJsonError(response, 500, "Error retrieving configuration");
        }
    }

    /// <summary>
    /// Handle POST /api/config - Update configuration
    /// </summary>
    private void HandlePostConfig(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            if (_configManager == null)
            {
                SendJsonError(response, 503, "Configuration manager not available");
                return;
            }

            // Read request body
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string body = reader.ReadToEnd();

            // Parse new configuration
            var newConfig = JsonSerializer.Deserialize<ConfigManager>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (newConfig == null)
            {
                SendJsonError(response, 400, "Invalid configuration");
                return;
            }

            // Get changes summary
            var changes = _configManager.GetChangeSummary(newConfig);

            // Apply changes
            string restartLevel = _configManager.ApplyChanges(newConfig);

            // Return result
            var result = new
            {
                success = true,
                restartLevel = restartLevel,
                changes = changes,
                message = restartLevel switch
                {
                    "instant" => "Configuration updated successfully (no restart needed)",
                    "smart" => "Configuration saved. Restart specific components to apply changes.",
                    "full" => "Configuration saved. Full restart required to apply all changes.",
                    _ => "No changes detected"
                }
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            SendJsonResponse(response, json);

            _logger?.LogInformation("[MediaHttpServer] Configuration updated: RestartLevel={RestartLevel}, Changes={ChangeCount}",
                restartLevel, changes.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError("[MediaHttpServer] Error updating config: {Message}", ex.Message);
            SendJsonError(response, 500, $"Error updating configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle GET /api/status - Return agent status
    /// </summary>
    private void HandleGetStatus(HttpListenerResponse response)
    {
        try
        {
            var status = new
            {
                timestamp = DateTime.Now,
                recording = new
                {
                    isRecording = _videoRecorder?.IsRecording ?? false,
                    currentFile = _videoRecorder?.CurrentRecordingPath,
                    workerRunning = _recorderWorker != null
                },
                upload = new
                {
                    workerRunning = _uploadWorker != null,
                    pendingVideos = _database?.GetPendingVideos(100)?.Count ?? 0
                },
                server = new
                {
                    port = _port,
                    isRunning = _isRunning
                }
            };

            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            SendJsonResponse(response, json);
        }
        catch (Exception ex)
        {
            _logger?.LogError("[MediaHttpServer] Error getting status: {Message}", ex.Message);
            SendJsonError(response, 500, "Error retrieving status");
        }
    }

    /// <summary>
    /// Handle POST /api/restart - Restart application
    /// </summary>
    private void HandleRestart(HttpListenerResponse response)
    {
        try
        {
            var result = new
            {
                success = true,
                message = "Application restart initiated..."
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            SendJsonResponse(response, json);

            _logger?.LogWarning("[MediaHttpServer] Application restart requested via Web UI");

            // Trigger restart after a short delay (to allow response to be sent)
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                Environment.Exit(0);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError("[MediaHttpServer] Error handling restart: {Message}", ex.Message);
            SendJsonError(response, 500, "Error initiating restart");
        }
    }

    /// <summary>
    /// Handle GET /config - Serve configuration HTML page
    /// </summary>
    private void HandleConfigPage(HttpListenerResponse response)
    {
        try
        {
            string html = GetConfigPageHTML();
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.StatusCode = 200;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception ex)
        {
            _logger?.LogError("[MediaHttpServer] Error serving config page: {Message}", ex.Message);
            SendError(response, 500, "Error loading configuration page");
        }
    }

    /// <summary>
    /// Send JSON response
    /// </summary>
    private void SendJsonResponse(HttpListenerResponse response, string json)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = 200;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    /// <summary>
    /// Send JSON error response
    /// </summary>
    private void SendJsonError(HttpListenerResponse response, int statusCode, string message)
    {
        var error = new { success = false, error = message };
        var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = statusCode;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
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

    /// <summary>
    /// Get configuration HTML page
    /// </summary>
    private string GetConfigPageHTML()
    {
        return """

<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Paneas Agent Configuration</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        .container {
            max-width: 900px;
            margin: 0 auto;
            background: white;
            border-radius: 12px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
            overflow: hidden;
        }
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }
        .header h1 { font-size: 28px; margin-bottom: 8px; }
        .header p { opacity: 0.9; font-size: 14px; }
        .content { padding: 30px; }
        .status-bar {
            display: flex;
            justify-content: space-around;
            margin-bottom: 30px;
            padding: 20px;
            background: #f8f9fa;
            border-radius: 8px;
        }
        .status-item { text-align: center; }
        .status-label { font-size: 12px; color: #666; text-transform: uppercase; margin-bottom: 5px; }
        .status-value { font-size: 20px; font-weight: bold; color: #333; }
        .section {
            margin-bottom: 30px;
            padding: 20px;
            border: 1px solid #e0e0e0;
            border-radius: 8px;
        }
        .section-title {
            font-size: 18px;
            font-weight: 600;
            margin-bottom: 15px;
            color: #333;
            display: flex;
            align-items: center;
        }
        .badge {
            display: inline-block;
            padding: 2px 8px;
            border-radius: 12px;
            font-size: 11px;
            font-weight: 600;
            margin-left: 10px;
        }
        .badge-green { background: #d4edda; color: #155724; }
        .badge-yellow { background: #fff3cd; color: #856404; }
        .badge-red { background: #f8d7da; color: #721c24; }
        .form-group {
            margin-bottom: 15px;
        }
        .form-label {
            display: block;
            margin-bottom: 5px;
            font-size: 14px;
            font-weight: 500;
            color: #555;
        }
        .form-input, .form-select {
            width: 100%;
            padding: 10px;
            border: 1px solid #ddd;
            border-radius: 6px;
            font-size: 14px;
            transition: border-color 0.2s;
        }
        .form-input:focus, .form-select:focus {
            outline: none;
            border-color: #667eea;
        }
        .form-checkbox {
            width: 18px;
            height: 18px;
            margin-right: 8px;
        }
        .btn {
            padding: 12px 24px;
            border: none;
            border-radius: 6px;
            font-size: 14px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s;
        }
        .btn-primary {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
        }
        .btn-primary:hover { transform: translateY(-2px); box-shadow: 0 5px 15px rgba(102, 126, 234, 0.4); }
        .btn-danger { background: #dc3545; color: white; margin-left: 10px; }
        .btn-danger:hover { background: #c82333; }
        .actions {
            display: flex;
            justify-content: space-between;
            margin-top: 30px;
            padding-top: 20px;
            border-top: 1px solid #e0e0e0;
        }
        .message {
            padding: 15px;
            border-radius: 6px;
            margin-bottom: 20px;
            display: none;
        }
        .message-success { background: #d4edda; color: #155724; border: 1px solid #c3e6cb; }
        .message-error { background: #f8d7da; color: #721c24; border: 1px solid #f5c6cb; }
        .message-warning { background: #fff3cd; color: #856404; border: 1px solid #ffeeba; }
        .loading { display: none; text-align: center; padding: 20px; }
        .spinner {
            border: 3px solid #f3f3f3;
            border-top: 3px solid #667eea;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            animation: spin 1s linear infinite;
            margin: 0 auto;
        }
        @keyframes spin {
            0% { transform: rotate(0deg); }
            100% { transform: rotate(360deg); }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Paneas Agent Configuration</h1>
            <p>Manage your agent settings with hot-reload support</p>
        </div>
        <div class='content'>
            <div class='message' id='message'></div>
            <div class='loading' id='loading'>
                <div class='spinner'></div>
                <p style='margin-top: 15px;'>Loading configuration...</p>
            </div>

            <div class='status-bar' id='statusBar' style='display: none;'>
                <div class='status-item'>
                    <div class='status-label'>Recording</div>
                    <div class='status-value' id='statusRecording'>-</div>
                </div>
                <div class='status-item'>
                    <div class='status-label'>Pending Uploads</div>
                    <div class='status-value' id='statusPending'>-</div>
                </div>
                <div class='status-item'>
                    <div class='status-label'>Completed</div>
                    <div class='status-value' id='statusCompleted'>-</div>
                </div>
            </div>

            <form id='configForm' style='display: none;'>
                <div class='section'>
                    <div class='section-title'>
                        General
                        <span class='badge badge-red'>Full Restart</span>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Mode</label>
                        <select name='mode' class='form-select'>
                            <option value='c2'>C2 Only</option>
                            <option value='autonomous'>Autonomous Only</option>
                            <option value='hybrid'>Hybrid (C2 + Autonomous)</option>
                        </select>
                    </div>
                </div>

                <div class='section'>
                    <div class='section-title'>
                        Recording Settings
                        <span class='badge badge-yellow'>Smart Restart</span>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Recording Profile</label>
                        <select name='recording.profile' class='form-input' id='recordingProfile'>
                            <option value='Performance'>Performance - Optimized for weak machines (~9 MB/min, 50% less CPU)</option>
                            <option value='Balanced'>Balanced - Balance between quality and performance (~12 MB/min, 33% less CPU)</option>
                            <option value='Quality'>Quality - Maximum quality (~16 MB/min, normal CPU usage)</option>
                        </select>
                        <small class='form-help' id='profileDescription'></small>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>
                            <input type='checkbox' name='recording.continuous' class='form-checkbox'>
                            Continuous Recording
                        </label>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Interval (minutes)</label>
                        <input type='number' name='recording.intervalMinutes' class='form-input' min='1'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Duration (minutes)</label>
                        <input type='number' name='recording.durationMinutes' class='form-input' min='1'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Segment Duration (seconds)</label>
                        <input type='number' name='recording.segmentSeconds' class='form-input' min='1'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>FPS</label>
                        <input type='number' name='recording.fps' class='form-input' min='1' max='60'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Video Bitrate (kbps)</label>
                        <input type='number' name='recording.videoBitrate' class='form-input' min='500'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>
                            <input type='checkbox' name='recording.captureAudio' class='form-checkbox'>
                            Capture Audio
                        </label>
                    </div>
                </div>

                <div class='section'>
                    <div class='section-title'>
                        Upload Settings
                        <span class='badge badge-green'>Instant</span>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>
                            <input type='checkbox' name='upload.enabled' class='form-checkbox'>
                            Upload Enabled
                        </label>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Poll Interval (seconds)</label>
                        <input type='number' name='upload.pollIntervalSeconds' class='form-input' min='1'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Max Concurrent Uploads</label>
                        <input type='number' name='upload.maxConcurrentUploads' class='form-input' min='1' max='10'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Max Retries</label>
                        <input type='number' name='upload.maxRetries' class='form-input' min='0'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Endpoint URL</label>
                        <input type='text' name='upload.endpoint' class='form-input'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>API Key</label>
                        <input type='text' name='upload.apiKey' class='form-input'>
                    </div>
                </div>

                <div class='section'>
                    <div class='section-title'>
                        C2 Settings
                        <span class='badge badge-yellow'>Smart Restart</span>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>
                            <input type='checkbox' name='c2.enabled' class='form-checkbox'>
                            C2 Enabled
                        </label>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Server URL</label>
                        <input type='text' name='c2.serverUrl' class='form-input'>
                    </div>
                    <div class='form-group'>
                        <label class='form-label'>Reconnect Delay (seconds)</label>
                        <input type='number' name='c2.reconnectDelaySeconds' class='form-input' min='1'>
                    </div>
                </div>

                <div class='actions'>
                    <button type='submit' class='btn btn-primary'>Save Configuration</button>
                    <button type='button' class='btn btn-danger' onclick='restartAgent()'>Restart Agent</button>
                </div>
            </form>
        </div>
    </div>

    <script>
        let currentConfig = null;

        async function loadConfig() {
            try {
                document.getElementById('loading').style.display = 'block';
                document.getElementById('configForm').style.display = 'none';

                const response = await fetch('/api/config');
                if (!response.ok) throw new Error('Failed to load configuration');

                currentConfig = await response.json();
                populateForm(currentConfig);
                await loadStatus();

                document.getElementById('loading').style.display = 'none';
                document.getElementById('configForm').style.display = 'block';
                document.getElementById('statusBar').style.display = 'flex';
            } catch (error) {
                showMessage('error', 'Error loading configuration: ' + error.message);
                document.getElementById('loading').style.display = 'none';
            }
        }

        async function loadStatus() {
            try {
                const response = await fetch('/api/status');
                if (!response.ok) return;

                const status = await response.json();
                document.getElementById('statusRecording').textContent = status.recording.isRecording ? 'Active' : 'Idle';
                document.getElementById('statusPending').textContent = status.upload.pendingVideos || 0;
                document.getElementById('statusCompleted').textContent = status.upload.completedVideos || 0;
            } catch (error) {
                console.error('Error loading status:', error);
            }
        }

        function populateForm(config) {
            // General
            document.querySelector('[name=\"mode\"]').value = config.mode;

            // Recording
            document.querySelector('[name=\"recording.profile\"]').value = config.recording.profile || 'Performance';
            document.querySelector('[name=\"recording.continuous\"]').checked = config.recording.continuous;
            document.querySelector('[name=\"recording.intervalMinutes\"]').value = config.recording.intervalMinutes;
            document.querySelector('[name=\"recording.durationMinutes\"]').value = config.recording.durationMinutes;
            document.querySelector('[name=\"recording.segmentSeconds\"]').value = config.recording.segmentSeconds;
            document.querySelector('[name=\"recording.fps\"]').value = config.recording.fps;
            document.querySelector('[name=\"recording.videoBitrate\"]').value = config.recording.videoBitrate;
            document.querySelector('[name=\"recording.captureAudio\"]').checked = config.recording.captureAudio;

            // Upload
            document.querySelector('[name=\"upload.enabled\"]').checked = config.upload.enabled;
            document.querySelector('[name=\"upload.pollIntervalSeconds\"]').value = config.upload.pollIntervalSeconds;
            document.querySelector('[name=\"upload.maxConcurrentUploads\"]').value = config.upload.maxConcurrentUploads;
            document.querySelector('[name=\"upload.maxRetries\"]').value = config.upload.maxRetries;
            document.querySelector('[name=\"upload.endpoint\"]').value = config.upload.endpoint;
            document.querySelector('[name=\"upload.apiKey\"]').value = config.upload.apiKey;

            // C2
            document.querySelector('[name=\"c2.enabled\"]').checked = config.c2.enabled;
            document.querySelector('[name=\"c2.serverUrl\"]').value = config.c2.serverUrl;
            document.querySelector('[name=\"c2.reconnectDelaySeconds\"]').value = config.c2.reconnectDelaySeconds;
        }

        function getFormData() {
            const formData = { ...currentConfig };

            // General
            formData.mode = document.querySelector('[name=\"mode\"]').value;

            // Recording
            formData.recording.profile = document.querySelector('[name=\"recording.profile\"]').value;
            formData.recording.continuous = document.querySelector('[name=\"recording.continuous\"]').checked;
            formData.recording.intervalMinutes = parseInt(document.querySelector('[name=\"recording.intervalMinutes\"]').value);
            formData.recording.durationMinutes = parseInt(document.querySelector('[name=\"recording.durationMinutes\"]').value);
            formData.recording.segmentSeconds = parseInt(document.querySelector('[name=\"recording.segmentSeconds\"]').value);
            formData.recording.fps = parseInt(document.querySelector('[name=\"recording.fps\"]').value);
            formData.recording.videoBitrate = parseInt(document.querySelector('[name=\"recording.videoBitrate\"]').value);
            formData.recording.captureAudio = document.querySelector('[name=\"recording.captureAudio\"]').checked;

            // Upload
            formData.upload.enabled = document.querySelector('[name=\"upload.enabled\"]').checked;
            formData.upload.pollIntervalSeconds = parseInt(document.querySelector('[name=\"upload.pollIntervalSeconds\"]').value);
            formData.upload.maxConcurrentUploads = parseInt(document.querySelector('[name=\"upload.maxConcurrentUploads\"]').value);
            formData.upload.maxRetries = parseInt(document.querySelector('[name=\"upload.maxRetries\"]').value);
            formData.upload.endpoint = document.querySelector('[name=\"upload.endpoint\"]').value;
            formData.upload.apiKey = document.querySelector('[name=\"upload.apiKey\"]').value;

            // C2
            formData.c2.enabled = document.querySelector('[name=\"c2.enabled\"]').checked;
            formData.c2.serverUrl = document.querySelector('[name=\"c2.serverUrl\"]').value;
            formData.c2.reconnectDelaySeconds = parseInt(document.querySelector('[name=\"c2.reconnectDelaySeconds\"]').value);

            return formData;
        }

        async function saveConfig(event) {
            event.preventDefault();

            try {
                const config = getFormData();

                const response = await fetch('/api/config', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(config)
                });

                if (!response.ok) throw new Error('Failed to save configuration');

                const result = await response.json();

                let messageType = 'success';
                let message = result.message;

                if (result.restartLevel === 'smart') {
                    messageType = 'warning';
                    message += ' You may want to restart the agent to apply all changes.';
                } else if (result.restartLevel === 'full') {
                    messageType = 'warning';
                    message += ' Full restart required! Use the \"Restart Agent\" button.';
                }

                if (result.changes && result.changes.length > 0) {
                    message += '\\n\\nChanges: ' + result.changes.join(', ');
                }

                showMessage(messageType, message);
                currentConfig = config;

            } catch (error) {
                showMessage('error', 'Error saving configuration: ' + error.message);
            }
        }

        async function restartAgent() {
            if (!confirm('Are you sure you want to restart the agent? This will interrupt any ongoing recordings.')) {
                return;
            }

            try {
                const response = await fetch('/api/restart', { method: 'POST' });
                if (!response.ok) throw new Error('Failed to initiate restart');

                showMessage('success', 'Agent restart initiated. The page will disconnect shortly...');

                // Wait a few seconds then try to reconnect
                setTimeout(() => {
                    window.location.reload();
                }, 5000);

            } catch (error) {
                showMessage('error', 'Error restarting agent: ' + error.message);
            }
        }

        function showMessage(type, message) {
            const messageEl = document.getElementById('message');
            messageEl.className = 'message message-' + type;
            messageEl.textContent = message;
            messageEl.style.display = 'block';

            setTimeout(() => {
                messageEl.style.display = 'none';
            }, 10000);
        }

        document.getElementById('configForm').addEventListener('submit', saveConfig);
        loadConfig();
        setInterval(loadStatus, 5000); // Refresh status every 5 seconds
    </script>
</body>
</html>
""";
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _listener.Close();
    }
}
