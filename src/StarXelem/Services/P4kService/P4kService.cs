using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarBreaker.FileSystem;
using StarBreaker.P4k;
using StarXelem.Models;

namespace StarXelem.Services;

public class P4kService : IP4kService
{
    private const string dataCorePath = "Data\\Game2.dcb";
    private readonly ILogger<P4kService> _logger;
    private P4kDirectoryNode? _p4KFile;
    private P4kFileModel? _selectedP4KFile;
    public const string DataP4k = "Data.p4k";
    public const string BuildManifest = "build_manifest.id";
    private readonly Dictionary<string, string> _locale = new();
    private readonly Dictionary<uint, DataCoreTypedRecord> _EntityClassDict = new();
    public static readonly string DefaultRSILauncherFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rsilauncher");
    public static readonly string DefaultStarCitizenFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Roberts Space Industries", "StarCitizen");
    private Task? _loadingLocalTask;
    private Task? _loadingDatabaseTask;


    public P4kDirectoryNode P4KFileSystem => _p4KFile ?? throw new InvalidOperationException("P4k file not open");

    public P4kFileModel? SelectedP4KFile
    {
        get => _selectedP4KFile;
        set
        {
            _selectedP4KFile = value;
            SelectedP4KFileChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<P4kFileModel?>? SelectedP4KFileChanged;

    public P4kService(ILogger<P4kService> logger)
    {
        _logger = logger;
    }

    public Task OpenP4k(string path, IProgress<double> p4kProgress, IProgress<double> fileSystemProgress)
    {
        if (_p4KFile != null)
        {
            _logger.LogWarning("P4k file already open");
            return Task.FromResult(_p4KFile);
        }

        return Task.Run(() =>
        {
            _p4KFile = P4kDirectoryNode.FromP4k(P4kFile.FromFile(path, p4kProgress), fileSystemProgress);
        });
    }
    
    public Task<IList<P4kFileModel>> LoadDefaultP4kLocations()
    {
        if (!TryGetInstallDirectory(out var currentInstallDirectory))
            currentInstallDirectory = DefaultStarCitizenFolder;

        return GetP4ksFromDirectoryAsync(currentInstallDirectory);
    }

    private async Task<IList<P4kFileModel>> GetP4ksFromDirectoryAsync(string installationPath)
    {
        var installations = new List<P4kFileModel>();
        if (!Directory.Exists(installationPath))
            return installations;

        var p4ks = Directory.GetFiles(installationPath, DataP4k, SearchOption.AllDirectories);
        if (p4ks.Length == 0)
        {
            _logger.LogError("No Data.p4k files found");
            return installations;
        }

        _logger.LogTrace("Found {Count} Data.p4k files", p4ks.Length);

        foreach (var p4k in p4ks)
        {
            var install = await GetInstallationInfo(p4k);
            
            if (null != install)
            {
                installations.Add(install);
            }
        }

        return installations;
    }

    /// <summary>
    /// Checks the RSI Launcher logs for a Star Citizen Install Directory
    /// </summary>
    /// <returns>The current Star Citizen install directory</returns>
    private bool TryGetInstallDirectory(out string dir)
    {
        dir = "";

        var launcherPath = DefaultRSILauncherFolder;
        if (!Directory.Exists(launcherPath))
        {
            _logger.LogError("Failed to find RSI Launcher directory");
            return false;
        }

        var logPath = Path.Combine(launcherPath, "logs", "log.log");

        if (!File.Exists(logPath))
        {
            _logger.LogError("Failed to find RSI Launcher log");
            return false;
        }

        foreach (var line in File.ReadLines(logPath))
        {
            if (!line.Contains("Installing Star Citizen"))
                continue;

            try
            {
                var strstart = line.IndexOf(" at ", StringComparison.InvariantCultureIgnoreCase) + " at ".Length;
                var strend = line.LastIndexOf("StarCitizen", StringComparison.InvariantCultureIgnoreCase) + "StarCitizen".Length;
                var installDirectory = line.Substring(strstart, strend - strstart);
                dir = installDirectory;
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse SC install directory from launcher log");
                return false;
            }
        }

        _logger.LogError("Failed to find SC install directory from launcher log");
        return false;
    }


    public Task<IList<P4kFileModel>> FindInstalledFiles()
    {
        return LoadDefaultP4kLocations();
    }

    public async Task<P4kFileModel?> GetInstallationInfo(string p4kPath)
    {
        var directoryName = Path.GetDirectoryName(p4kPath);
        if (directoryName is null)
        {
            _logger.LogError("Failed to get directory name for {Path}", p4kPath);
            return null;
        }

        BuildManifestModel? manifest = null;
        try
        {
            if (File.Exists(Path.Combine(directoryName, BuildManifest)))
            {
                await using var fileStream = File.Open(Path.Combine(directoryName, BuildManifest), FileMode.Open, FileAccess.Read);
                manifest = await JsonSerializer.DeserializeAsync<BuildManifestModel>(fileStream);
            }
        }
        catch
        {
            //fine to ignore
        }

        return new P4kFileModel
        {
            ChannelName = new DirectoryInfo(directoryName).Name,
            Path = p4kPath,
            Manifest = manifest
        };

    }

    private Task LoadLangFileIfNeeded()
    {
        if (null == _loadingLocalTask)
        {
            _loadingLocalTask = Task.Run(async () =>
            {
                // chargement du fichier p4k
                await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>());
                //  chargement de la traduction
                var globalEntry = P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
                _locale.Clear();
                using (var sr = new StreamReader(globalEntry, Encoding.UTF8, true))
                {
                    while (await sr.ReadLineAsync() is { } line)
                    {

                        if (!String.IsNullOrEmpty(line))
                        {
                            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                            var key = parts[0];
                            var value = parts[1];

                            if (key.EndsWith(",P"))
                                key = key[..^2];
                            _locale.Add($"@{key}", value);
                        }
                    }
                }
                
                await globalEntry.DisposeAsync();
            });
        }
        
        return _loadingLocalTask;
    }

    public async Task<string?> GetLocaleValue(string? key)
    {
        if (null == SelectedP4KFile || null == key)
        {
            return null;
        }
        
        await LoadLangFileIfNeeded();

        return _locale.GetValueOrDefault(key, key);
    }

    private Task LoadDatabaseIfNeeded()
    {
        if (null == _loadingDatabaseTask)
        {
            _loadingDatabaseTask = Task.Run(async () =>
            {
                // chargement du fichier p4k
                await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>());
                // Chargement des données
                var entry = P4KFileSystem.OpenRead(dataCorePath);
                var dcb = new DataCoreDatabase(entry);
                var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
                await entry.DisposeAsync();
                
                var allRecords = df.DataCore.Database.MainRecords
                    .AsParallel()
                    .Select(x => df.GetFromRecord(x))
                    .ToList();


                foreach (var record in allRecords.Where(r => r.Data is EntityClassDefinition))
                {
                    var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record.RecordId]));

                    _EntityClassDict.Add(crc, record);
                }
            });
        }
        
        return _loadingDatabaseTask;        
    }
    
    public async Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc)
    {
        await LoadDatabaseIfNeeded();
        
        return _EntityClassDict.GetValueOrDefault(guidCrc);
    }
}