using System.Diagnostics;
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
    private CancellationTokenSource _cancellationTokenSource = new();
    private Task _openP4kTask;


    public P4kDirectoryNode P4KFileSystem => _p4KFile ?? throw new InvalidOperationException("P4k file not open");

    public P4kFileModel? SelectedP4KFile
    {
        get => _selectedP4KFile;
        set
        {
            _selectedP4KFile = value;
            ResetSelectedFile();
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

        // On réinitialise la source de token vu que c'est un nouveau fichier
        _cancellationTokenSource = new CancellationTokenSource();
        _openP4kTask = Task.Run(() =>
        {
            _p4KFile = P4kDirectoryNode.FromP4k(P4kFile.FromFile(path, p4kProgress), fileSystemProgress);
        }, _cancellationTokenSource.Token)
            // Une fois ouvert on supprime la task
            .ContinueWith(t => _openP4kTask = null);
        
        
        return _openP4kTask;
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
            var install = await GetInstallationInfo(p4k).ConfigureAwait(false);
            
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
                var sw = Stopwatch.StartNew();
                // chargement du fichier p4k
                await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
                //  chargement de la traduction
                var globalEntry = P4KFileSystem.OpenRead(@"Data\Localization\english\global.ini");
                _locale.Clear();
                using (var sr = new StreamReader(globalEntry, Encoding.UTF8, true))
                {
                    while (await sr.ReadLineAsync().ConfigureAwait(false) is { } line)
                    {
                        if (_cancellationTokenSource.IsCancellationRequested)
                        {
                            // on demande l'arrêt du traitement, on s'arrête la
                            break;
                        }
                        
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
                sw.Stop();
                _logger.LogTrace("Extracted all locale values in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
                
                await globalEntry.DisposeAsync().ConfigureAwait(false);
            }, _cancellationTokenSource.Token);
        }
        
        return _loadingLocalTask;
    }

    public async Task<string?> GetLocaleValue(string? key)
    {
        if (null == SelectedP4KFile || null == key)
        {
            return null;
        }
        
        await LoadLangFileIfNeeded().ConfigureAwait(false);

        return _locale.GetValueOrDefault(key, key);
    }

    private Task LoadDatabaseIfNeeded()
    {
        if (null == _loadingDatabaseTask)
        {
            _loadingDatabaseTask = Task.Run(async () =>
            {
                // chargement du fichier p4k
                await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
                // Chargement des données
                var entry = P4KFileSystem.OpenRead(dataCorePath);
                var dcb = new DataCoreDatabase(entry);
                var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
                await entry.DisposeAsync().ConfigureAwait(false);

                var sw = Stopwatch.StartNew();
                var allRecords = df.DataCore.Database.MainRecords
                    .AsParallel()
                    .Select(x =>
                    {
                        if (_cancellationTokenSource.IsCancellationRequested) return null;
                        return df.GetFromRecord(x);
                    })
                    ;
                sw.Stop();
                _logger.LogTrace("Extracted all records in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);

                sw = Stopwatch.StartNew();
                foreach (var record in allRecords.Where(r => r?.Data is EntityClassDefinition or StarMapObject or ContractGenerator))
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        // une demande d'annulation est arrivée
                        break;
                    }
                    
                    var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record.RecordId]));

                    _EntityClassDict.Add(crc, record);
                }
                sw.Stop();
                _logger.LogTrace("Extracted all entity classes in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
            }, _cancellationTokenSource.Token);
        }
        
        return _loadingDatabaseTask;        
    }
    
    public async Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc)
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);
        
        return _EntityClassDict.GetValueOrDefault(guidCrc);
    }

    public async IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinition()
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        foreach (var record in _EntityClassDict.Values)
        {
            if (record.Data is EntityClassDefinition)
                yield return record;
        }
    }

    public Task FillDataCache()
    {
        var task1 = LoadDatabaseIfNeeded();
        var task2 = LoadLangFileIfNeeded();
        
        return Task.WhenAll(task1, task2);
    }

    public async Task<List<DataCoreTypedRecord>> GetAllContractGenerator()
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        var result = new List<DataCoreTypedRecord>(50);

        foreach (var record in _EntityClassDict.Values)
        {
            if (record.Data is ContractGenerator)
            {
                result.Add(record);
            }
        }
        
        return result;
    }

    public async Task<string?> GetEntityClassName(EntityClassDefinition? entityClass)
    {
        var key = entityClass?.Components?.OfType<SAttachableComponentParams>().FirstOrDefault()?.AttachDef.Localization.Name;

        if (null == key)
        {
            return null;
        }
        
        await LoadLangFileIfNeeded().ConfigureAwait(false);
        return _locale.GetValueOrDefault(key);
    }

    public async Task<DataCoreTypedRecord> GetRecordWithFullHistory(CigGuid recordId)
    {
        // chargement du fichier p4k
        await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
        // Chargement des données
        var entry = P4KFileSystem.OpenRead(dataCorePath);
        var dcb = new DataCoreDatabase(entry);
        var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
        await entry.DisposeAsync().ConfigureAwait(false);

        var sw = Stopwatch.StartNew();
        var oldval = DataCoreBinaryGenerated.s_maxRecursiveLoad;
        try
        {
            // On va se limiter à 3 déjà
            DataCoreBinaryGenerated.s_maxRecursiveLoad = 3;
            var record = df.GetFromRecord(recordId);
            
            sw.Stop();
            //_logger.LogTrace("Extracted record {recordId} with full recursion in {ElapsedMilliseconds}ms", recordId, sw.ElapsedMilliseconds);
            return record;
        }
        finally
        {
            DataCoreBinaryGenerated.s_maxRecursiveLoad = oldval;
        }
    }

    private void ResetSelectedFile()
    {
        // stop previous loading if any
        _cancellationTokenSource.Cancel();
        
        // clear caches
        _EntityClassDict.Clear();
        _locale.Clear();
        _loadingLocalTask = null;
        _loadingDatabaseTask = null;
        
        
        // reset file
        _p4KFile = null;
    }
}