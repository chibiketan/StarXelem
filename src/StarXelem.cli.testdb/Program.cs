using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
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
    var settingsLogger = loggerFactory.CreateLogger<RegistrySettingsService>();
    var dbService = new LocalDatabaseService(p4kService, dbLogger, new RegistrySettingsService(settingsLogger), autoRebuild: false);

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

    // Check specific weapons
    using var conn = new SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT LocalizedName, TypeName, SubTypeName, DamagePhysical, DamageEnergy, DamageDistortion, DamageThermal, DamageBiochemical, DamageStun 
        FROM ScItems 
        WHERE LocalizedName LIKE '%Jericho%' OR LocalizedName LIKE '%Supremacy%' OR LocalizedName LIKE '%Suckerpunch%'
        LIMIT 20";
    var reader = cmd.ExecuteReader();
    Console.WriteLine("\n=== Specific weapons ===");
    while (reader.Read())
    {
        var name = reader.GetString(0);
        var typeName = reader.GetString(1);
        var subTypeName = reader.GetString(2);
        var hasData = false;
        var vals = new string[6];
        for (int i = 0; i < 6; i++)
        {
            if (reader.IsDBNull(i + 3))
                vals[i] = "?";
            else
            {
                vals[i] = reader.GetFloat(i + 3).ToString("F1");
                hasData = true;
            }
        }
        if (hasData)
            Console.WriteLine($"  {name} [{typeName}/{subTypeName}] | P:{vals[0]} E:{vals[1]} D:{vals[2]} T:{vals[3]} B:{vals[4]} S:{vals[5]}");
        else
            Console.WriteLine($"  {name} [{typeName}/{subTypeName}] | (no damage data)");
    }
    reader.Dispose();

    // Check loadout entries for Jericho/Suckerpunch
    Console.WriteLine("\n=== Loadout entries ===");
    cmd.CommandText = @"SELECT DisplayName, WeaponType, AlphaDamage FROM ShipLoadoutEntries WHERE DisplayName LIKE '%Jericho%' OR DisplayName LIKE '%Suckerpunch%' LIMIT 10";
    reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        Console.WriteLine($"  {reader.GetString(0)} | {reader.GetString(1)} | alpha:{(reader.IsDBNull(2) ? "?" : reader.GetFloat(2))}");
    }
    reader.Dispose();

    // Count overall damage stats
    cmd.CommandText = @"SELECT 
        (SELECT COUNT(*) FROM ScItems WHERE DamagePhysical IS NOT NULL OR DamageEnergy IS NOT NULL OR DamageDistortion IS NOT NULL OR DamageThermal IS NOT NULL OR DamageBiochemical IS NOT NULL OR DamageStun IS NOT NULL) as with_damage,
        COUNT(*) as total 
        FROM ScItems";
    reader = cmd.ExecuteReader();
    if (reader.Read())
    {
        Console.WriteLine($"\n=== Damage stats ===");
        Console.WriteLine($"Items with damage: {reader.GetInt32(0)} / {reader.GetInt32(1)}");
    }
}
