using PaneasMonitorService;

var builder = Host.CreateApplicationBuilder(args);

// Load service-specific configuration
builder.Configuration.AddJsonFile("appsettings.Service.json", optional: false, reloadOnChange: true);

// Configure Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PaneasMonitorService";
});

// Add MonitorService as hosted service
builder.Services.AddHostedService<MonitorService>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "PaneasMonitorService";
    settings.LogName = "Application";
});

// Add file logging
var logDirectory = Path.Combine("C:\\ProgramData\\C2Agent\\logs");
Directory.CreateDirectory(logDirectory);
var logFilePath = Path.Combine(logDirectory, $"console-{DateTime.Now:yyyyMMdd}.log");
builder.Logging.AddFile(logFilePath);

var host = builder.Build();
host.Run();
