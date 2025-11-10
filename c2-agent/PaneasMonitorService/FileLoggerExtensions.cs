using Microsoft.Extensions.Logging;

namespace PaneasMonitorService;

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath)
    {
        builder.AddProvider(new FileLoggerProvider(filePath));
        return builder;
    }
}

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly object _lock = new object();

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _filePath, _lock);
    }

    public void Dispose() { }
}

public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _filePath;
    private readonly object _lock;

    public FileLogger(string categoryName, string filePath, object lockObj)
    {
        _categoryName = categoryName;
        _filePath = filePath;
        _lock = lockObj;
    }

    public IDisposable BeginScope<TState>(TState state) => null!;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] {_categoryName}: {message}";

        if (exception != null)
        {
            logEntry += Environment.NewLine + exception.ToString();
        }

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_filePath, logEntry + Environment.NewLine);
            }
            catch
            {
                // Ignore file write errors to prevent logging from crashing the service
            }
        }
    }
}
