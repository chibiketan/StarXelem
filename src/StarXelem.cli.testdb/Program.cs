using Microsoft.Extensions.Logging;
using StarXelem.Models;
using StarXelem.Services;

await RunAsync();

async Task RunAsync()
{
    var loggerFactory = LoggerFactory.Create(b =>
    {
        b.SetMinimumLevel(LogLevel.Information);
        b.AddConsole();
    });

    var p4kLogger = loggerFactory.CreateLogger<P4kService>();
    var dbLogger = loggerFactory.CreateLogger<LocalDatabaseService>();

    var p4kService = new P4kService(p4kLogger);
    var dbService = new LocalDatabaseService(p4kService, dbLogger, autoRebuild: false);

    string p4kPath;

    if (args.Length > 0)
    {
        p4kPath = args[0];
        if (!File.Exists(p4kPath))
        {
            p4kLogger.LogError("P4K file not found: {Path}", p4kPath);
            return;
        }
        p4kService.SelectedP4KFile = new P4kFileModel { ChannelName = "Custom", Path = p4kPath };
    }
    else
    {
        p4kLogger.LogInformation("No P4K path provided, auto-discovering...");
        var locations = await p4kService.LoadDefaultP4kLocations();
        if (locations.Count == 0)
        {
            p4kLogger.LogError("No P4K locations found. Provide a path as CLI argument.");
            return;
        }
        p4kPath = locations[0].Path;
        p4kLogger.LogInformation("Using P4K: {Path}", p4kPath);
        p4kService.SelectedP4KFile = locations[0];
    }

    var p4kProgress = new Progress<double>();
    var fsProgress = new Progress<double>();

    await p4kService.OpenP4k(p4kPath, p4kProgress, fsProgress);

    await dbService.RebuildDbAsync();

    var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarXelem", "database.db");
    loggerFactory.CreateLogger<Program>().LogInformation("BDD reconstruite avec succes: {DbPath}", dbPath);
}
