using System.Text.Json;
using Microsoft.Extensions.Logging;
using Agent.Workers;

namespace Agent.Configuration;

/// <summary>
/// Enhanced configuration manager with hot-reload support
/// Provides 3 levels of configuration changes: Instant, Smart Restart, Full Restart
/// </summary>
public class ConfigurationManager
{
    private readonly ILogger<ConfigurationManager> _logger;
    private readonly string _configPath;
    private ConfigManager _currentConfig;
    private readonly object _lock = new object();

    // References to components that need updates
    private VideoRecorderWorker? _recorderWorker;
    private UploadWorker? _uploadWorker;
    private SseClient? _sseClient;

    public ConfigurationManager(string configPath, ILogger<ConfigurationManager> logger)
    {
        _configPath = configPath;
        _logger = logger;
        _currentConfig = ConfigManager.LoadFromFile(configPath);
    }

    /// <summary>
    /// Get current configuration (thread-safe)
    /// </summary>
    public ConfigManager Current
    {
        get
        {
            lock (_lock)
            {
                return _currentConfig;
            }
        }
    }

    /// <summary>
    /// Register components for hot-reload
    /// </summary>
    public void RegisterComponents(
        VideoRecorderWorker? recorderWorker = null,
        UploadWorker? uploadWorker = null,
        SseClient? sseClient = null)
    {
        _recorderWorker = recorderWorker;
        _uploadWorker = uploadWorker;
        _sseClient = sseClient;
    }

    /// <summary>
    /// Apply configuration changes with appropriate reload level
    /// Returns: restart level ("none", "smart", "full")
    /// </summary>
    public string ApplyChanges(ConfigManager newConfig)
    {
        lock (_lock)
        {
            var restartLevel = DetectRestartLevel(_currentConfig, newConfig);

            _logger.LogInformation("[ConfigurationManager] Applying changes with restart level: {RestartLevel}", restartLevel);

            switch (restartLevel)
            {
                case "instant":
                    ApplyInstantChanges(newConfig);
                    break;

                case "smart":
                    ApplySmartRestart(newConfig);
                    break;

                case "full":
                    ApplyFullRestart(newConfig);
                    break;
            }

            return restartLevel;
        }
    }

    /// <summary>
    /// Detect which level of restart is needed
    /// </summary>
    private string DetectRestartLevel(ConfigManager current, ConfigManager newConfig)
    {
        // FULL RESTART: Changes that require complete application restart
        if (current.Mode != newConfig.Mode ||
            current.Storage.BasePath != newConfig.Storage.BasePath ||
            current.Database.Path != newConfig.Database.Path)
        {
            return "full";
        }

        // SMART RESTART: Changes that require stopping/restarting specific components
        if (current.Recording.Continuous != newConfig.Recording.Continuous ||
            current.Recording.SegmentSeconds != newConfig.Recording.SegmentSeconds ||
            current.Recording.FPS != newConfig.Recording.FPS ||
            current.Recording.VideoBitrate != newConfig.Recording.VideoBitrate ||
            current.Recording.CaptureAudio != newConfig.Recording.CaptureAudio ||
            current.C2.Enabled != newConfig.C2.Enabled ||
            current.C2.ServerUrl != newConfig.C2.ServerUrl)
        {
            return "smart";
        }

        // INSTANT: Changes that can be applied immediately (in-memory variable updates)
        if (current.Recording.IntervalMinutes != newConfig.Recording.IntervalMinutes ||
            current.Recording.DurationMinutes != newConfig.Recording.DurationMinutes ||
            current.Upload.Enabled != newConfig.Upload.Enabled ||
            current.Upload.PollIntervalSeconds != newConfig.Upload.PollIntervalSeconds ||
            current.Upload.MaxConcurrentUploads != newConfig.Upload.MaxConcurrentUploads ||
            current.Upload.MaxRetries != newConfig.Upload.MaxRetries ||
            current.Upload.Endpoint != newConfig.Upload.Endpoint ||
            current.Upload.ApiKey != newConfig.Upload.ApiKey ||
            current.Tus.TusServerUrl != newConfig.Tus.TusServerUrl ||
            current.Tus.MaxRetries != newConfig.Tus.MaxRetries ||
            current.Tus.RetryDelayMs != newConfig.Tus.RetryDelayMs ||
            current.C2.ReconnectDelaySeconds != newConfig.C2.ReconnectDelaySeconds)
        {
            return "instant";
        }

        return "none";
    }

    /// <summary>
    /// Apply instant changes (update in-memory variables only)
    /// </summary>
    private void ApplyInstantChanges(ConfigManager newConfig)
    {
        _logger.LogInformation("[ConfigurationManager] Applying instant changes...");

        // Update recording intervals
        if (_recorderWorker != null)
        {
            _recorderWorker.UpdateIntervals(
                newConfig.Recording.IntervalMinutes,
                newConfig.Recording.DurationMinutes
            );
        }

        // Update upload settings
        if (_uploadWorker != null)
        {
            _uploadWorker.UpdateSettings(
                enabled: newConfig.Upload.Enabled,
                pollIntervalSeconds: newConfig.Upload.PollIntervalSeconds,
                maxConcurrentUploads: newConfig.Upload.MaxConcurrentUploads,
                maxRetries: newConfig.Upload.MaxRetries,
                endpoint: newConfig.Upload.Endpoint,
                apiKey: newConfig.Upload.ApiKey,
                tusServerUrl: newConfig.Tus.TusServerUrl,
                tusMaxRetries: newConfig.Tus.MaxRetries,
                tusRetryDelayMs: newConfig.Tus.RetryDelayMs
            );
        }

        _currentConfig = newConfig;
        _logger.LogInformation("[ConfigurationManager] Instant changes applied successfully");
    }

    /// <summary>
    /// Apply smart restart (stop/restart specific components)
    /// </summary>
    private void ApplySmartRestart(ConfigManager newConfig)
    {
        _logger.LogInformation("[ConfigurationManager] Applying smart restart...");

        // Note: Smart restart requires async operations, so we'll just save config
        // The actual restart will be handled by the caller
        _currentConfig = newConfig;
        newConfig.SaveToFile(_configPath);

        _logger.LogInformation("[ConfigurationManager] Configuration saved. Smart restart required.");
    }

    /// <summary>
    /// Apply full restart (save config and exit application)
    /// </summary>
    private void ApplyFullRestart(ConfigManager newConfig)
    {
        _logger.LogInformation("[ConfigurationManager] Applying full restart...");

        _currentConfig = newConfig;
        newConfig.SaveToFile(_configPath);

        _logger.LogInformation("[ConfigurationManager] Configuration saved. Full restart required.");
    }

    /// <summary>
    /// Get configuration change summary for display
    /// </summary>
    public List<string> GetChangeSummary(ConfigManager newConfig)
    {
        var changes = new List<string>();

        lock (_lock)
        {
            var current = _currentConfig;

            // Mode changes
            if (current.Mode != newConfig.Mode)
                changes.Add($"Mode: {current.Mode} → {newConfig.Mode}");

            // Recording changes
            if (current.Recording.Continuous != newConfig.Recording.Continuous)
                changes.Add($"Continuous Recording: {current.Recording.Continuous} → {newConfig.Recording.Continuous}");
            if (current.Recording.IntervalMinutes != newConfig.Recording.IntervalMinutes)
                changes.Add($"Recording Interval: {current.Recording.IntervalMinutes}min → {newConfig.Recording.IntervalMinutes}min");
            if (current.Recording.DurationMinutes != newConfig.Recording.DurationMinutes)
                changes.Add($"Recording Duration: {current.Recording.DurationMinutes}min → {newConfig.Recording.DurationMinutes}min");
            if (current.Recording.SegmentSeconds != newConfig.Recording.SegmentSeconds)
                changes.Add($"Segment Duration: {current.Recording.SegmentSeconds}s → {newConfig.Recording.SegmentSeconds}s");
            if (current.Recording.FPS != newConfig.Recording.FPS)
                changes.Add($"FPS: {current.Recording.FPS} → {newConfig.Recording.FPS}");
            if (current.Recording.VideoBitrate != newConfig.Recording.VideoBitrate)
                changes.Add($"Bitrate: {current.Recording.VideoBitrate}kbps → {newConfig.Recording.VideoBitrate}kbps");
            if (current.Recording.CaptureAudio != newConfig.Recording.CaptureAudio)
                changes.Add($"Capture Audio: {current.Recording.CaptureAudio} → {newConfig.Recording.CaptureAudio}");

            // Upload changes
            if (current.Upload.Enabled != newConfig.Upload.Enabled)
                changes.Add($"Upload Enabled: {current.Upload.Enabled} → {newConfig.Upload.Enabled}");
            if (current.Upload.PollIntervalSeconds != newConfig.Upload.PollIntervalSeconds)
                changes.Add($"Upload Poll Interval: {current.Upload.PollIntervalSeconds}s → {newConfig.Upload.PollIntervalSeconds}s");
            if (current.Upload.MaxConcurrentUploads != newConfig.Upload.MaxConcurrentUploads)
                changes.Add($"Max Concurrent Uploads: {current.Upload.MaxConcurrentUploads} → {newConfig.Upload.MaxConcurrentUploads}");
            if (current.Upload.Endpoint != newConfig.Upload.Endpoint)
                changes.Add($"Upload Endpoint: {current.Upload.Endpoint} → {newConfig.Upload.Endpoint}");

            // C2 changes
            if (current.C2.Enabled != newConfig.C2.Enabled)
                changes.Add($"C2 Enabled: {current.C2.Enabled} → {newConfig.C2.Enabled}");
            if (current.C2.ServerUrl != newConfig.C2.ServerUrl)
                changes.Add($"C2 Server: {current.C2.ServerUrl} → {newConfig.C2.ServerUrl}");
            if (current.C2.ReconnectDelaySeconds != newConfig.C2.ReconnectDelaySeconds)
                changes.Add($"C2 Reconnect Delay: {current.C2.ReconnectDelaySeconds}s → {newConfig.C2.ReconnectDelaySeconds}s");

            // Storage changes
            if (current.Storage.BasePath != newConfig.Storage.BasePath)
                changes.Add($"Storage Path: {current.Storage.BasePath} → {newConfig.Storage.BasePath}");
        }

        return changes;
    }
}
