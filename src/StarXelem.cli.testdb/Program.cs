using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using StarXelem.Cli.TestDb;
using StarXelem.Data;
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
    var factory = new DbContextFactory();
    var dbService = new LocalDatabaseService(p4kService, dbLogger, new RegistrySettingsService(settingsLogger), factory, autoRebuild: false);

    var probeFps = args.Contains("--probe-fps");
    var positionalArgs = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

    string p4kPath;

    if (positionalArgs.Length > 0)
    {
        p4kPath = positionalArgs[0];
        if (!File.Exists(p4kPath))
        {
            // Console.Error plutôt que le logger : la sortie asynchrone du logger console est perdue quand le processus se termine aussitôt.
            Console.Error.WriteLine($"Fichier P4K introuvable : {p4kPath}");
            if (args.Contains("--probe-grpc"))
            {
                Console.Error.WriteLine("Les options de la sonde s'écrivent --cle=valeur (et non --cle valeur) : une valeur isolée est prise pour le chemin du P4K.");
            }

            Environment.ExitCode = 1;
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

    // Le P4K n'est pas ouvert : la sonde n'en utilise que le chemin, pour localiser loginData.json.
    if (args.Contains("--probe-grpc"))
    {
        Environment.ExitCode = await EntityQueryProbe.RunAsync(p4kPath, args);
        return;
    }

    var p4kProgress = new Progress<double>();
    var fsProgress = new Progress<double>();

    await p4kService.OpenP4k(p4kPath, p4kProgress, fsProgress);

    if (probeFps)
    {
        var probeTag = positionalArgs.Length > 1 ? positionalArgs[1] : "hdgw_rifle_ballistic_01";
        await FpsWeaponProbe.RunAsync(p4kService, probeTag, finalDepth: 3);
        return;
    }

    await dbService.RebuildDbAsync();

    loggerFactory.CreateLogger<Program>().LogInformation("BDD reconstruite avec succes: {DbPath}", factory.DbPath);

    // Check specific weapons
    using var conn = new SqliteConnection($"Data Source={factory.DbPath}");
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

    // Armes FPS : contrôle du regroupement et des valeurs de l'Arlington
    Console.WriteLine("\n=== Armes FPS ===");
    cmd.CommandText = "SELECT COUNT(*), SUM(VariantCount), SUM(HasSecondaryFire) FROM FpsWeapons";
    reader = cmd.ExecuteReader();
    if (reader.Read())
    {
        Console.WriteLine($"  {reader.GetInt32(0)} modèles regroupés depuis {reader.GetInt32(1)} variantes, dont {reader.GetInt32(2)} avec tir secondaire");
    }
    reader.Dispose();

    cmd.CommandText = @"SELECT LocalizedName, WeaponClass, AmmoFamily, VariantCount, MagazineSize,
            PrimaryDamagePerShot, PrimaryDamageType, PrimaryPelletCount, PrimaryFireRate,
            PrimaryMaxRange, PrimaryEffectiveRange, PrimaryDamageFloorRange,
            SecondaryDamagePerShot, SecondaryDamageType, SecondaryPelletCount, SecondaryMaxRange,
            RepoolUnstowDuration, RepoolBulletsPerSecond
        FROM FpsWeapons WHERE Id = 'hdgw_rifle_ballistic_01'";
    reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        Console.WriteLine($"  {reader.GetString(0)} [{reader.GetString(1)}/{reader.GetString(2)}] "
            + $"{reader.GetInt32(3)} variantes, chargeur {reader.GetInt32(4)}");
        Console.WriteLine($"    principal : {reader.GetFloat(5)} {reader.GetString(6)} ×{reader.GetInt32(7)} "
            + $"cadence {reader.GetInt32(8)} | max {reader.GetFloat(9)}m | pleins {reader.GetFloat(10)}m | plancher {reader.GetFloat(11)}m");
        Console.WriteLine($"    secondaire: {reader.GetFloat(12)} {reader.GetString(13)} ×{reader.GetInt32(14)} | max {reader.GetFloat(15)}m");
        Console.WriteLine($"    extraction: {reader.GetFloat(16)}s, {reader.GetInt32(17)} balles/s");
    }
    reader.Dispose();

    cmd.CommandText = @"SELECT LocalizedName, PrimaryDamagePerShot, SecondaryDamagePerShot
        FROM FpsWeapons WHERE HasSecondaryFire = 1 OR PrimaryDamagePerShot IS NULL ORDER BY LocalizedName";
    reader = cmd.ExecuteReader();
    Console.WriteLine("  armes à double tir ou sans dégâts principaux :");
    while (reader.Read())
    {
        var primary = reader.IsDBNull(1) ? "NULL" : reader.GetFloat(1).ToString("0.##");
        var secondary = reader.IsDBNull(2) ? "—" : reader.GetFloat(2).ToString("0.##");
        Console.WriteLine($"    {reader.GetString(0),-34} principal={primary,-8} secondaire={secondary}");
    }
    reader.Dispose();

    cmd.CommandText = @"SELECT LocalizedName, Id, VariantCount, MagazineSize
        FROM FpsWeapons WHERE LocalizedName LIKE '%P8-AR%' OR LocalizedName LIKE '%Animus%' ORDER BY LocalizedName";
    reader = cmd.ExecuteReader();
    Console.WriteLine("  contrôle des anomalies corrigées (P8-AR regroupé, Animus chargeur) :");
    while (reader.Read())
    {
        var mag = reader.IsDBNull(3) ? "—" : reader.GetInt32(3).ToString();
        Console.WriteLine($"    {reader.GetString(0),-34} groupe={reader.GetString(1),-34} variantes={reader.GetInt32(2)} chargeur={mag}");
    }
    reader.Dispose();

    cmd.CommandText = @"SELECT LocalizedName, PrimaryDamagePerShot, PrimaryExplosiveDamage,
            SecondaryDamagePerShot, SecondaryExplosiveDamage
        FROM FpsWeapons WHERE PrimaryExplosiveDamage IS NOT NULL OR SecondaryExplosiveDamage IS NOT NULL
        ORDER BY PrimaryExplosiveDamage DESC";
    reader = cmd.ExecuteReader();
    Console.WriteLine("  armes explosives (direct / explosif) :");
    while (reader.Read())
    {
        string F(int i) => reader.IsDBNull(i) ? "—" : reader.GetFloat(i).ToString("0.##");
        Console.WriteLine($"    {reader.GetString(0),-34} principal={F(1)} ({F(2)})   secondaire={F(3)} ({F(4)})");
    }
    reader.Dispose();

    cmd.CommandText = "SELECT WeaponClass, COUNT(*) FROM FpsWeapons GROUP BY WeaponClass ORDER BY 2 DESC";
    reader = cmd.ExecuteReader();
    Console.WriteLine("  répartition par classe :");
    while (reader.Read())
    {
        Console.WriteLine($"    {reader.GetString(0),-12} {reader.GetInt32(1)}");
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
