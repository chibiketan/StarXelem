using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

public class P4kService : IP4kService, INotifyPropertyChanged
{
    private const string dataCorePath = "Data\\Game2.dcb";
    private readonly ILogger<P4kService> _logger;
    private P4kDirectoryNode? _p4KFile;
    private P4kFileModel? _selectedP4KFile;
    public const string DataP4k = "Data.p4k";
    public const string BuildManifest = "build_manifest.id";
    private readonly Dictionary<string, string> _locale = new();
    private readonly Dictionary<uint, CacheEntry> _EntityClassDict = new();
    private readonly Dictionary<CigGuid, CacheEntry> _entityClassGuidDict = new();
    public static readonly string DefaultRSILauncherFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rsilauncher");
    public static readonly string DefaultStarCitizenFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Roberts Space Industries", "StarCitizen");
    private Task? _loadingLocalTask;
    private Task? _loadingDatabaseTask;
    private CancellationTokenSource _cancellationTokenSource = new();
    private Task? _openP4kTask;
    private DataForge<DataCoreTypedRecord> df;
    private string? _lastErrorMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public enum P4kFileLoadState
    {
        NotLoaded,
        Loading,
        Loaded,
        Cancelled,
        CacheLoading,
        CacheLoaded,
        Error
    }

    private P4kFileLoadState _fileLoadState = P4kFileLoadState.NotLoaded;

    public P4kFileLoadState FileLoadState
    {
        get => _fileLoadState;
        private set => SetFileLoadState(value);
    }

    public string? GetLastErrorMessage()
    {
        return _fileLoadState == P4kFileLoadState.Error ? _lastErrorMessage : null;
    }

    private void SetFileLoadState(P4kFileLoadState newState)
    {
        if (_fileLoadState == newState) return;
        _fileLoadState = newState;
        OnPropertyChanged(nameof(FileLoadState));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


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
            // _logger.LogWarning("P4k file already open");
            if (FileLoadState != P4kFileLoadState.Loaded)
            {
                FileLoadState = P4kFileLoadState.Loaded;
            }
            return Task.FromResult(_p4KFile);
        }
        
        // On réinitialise la source de token vu que c'est un nouveau fichier
        _cancellationTokenSource = new CancellationTokenSource();
        _lastErrorMessage = null;
        FileLoadState = P4kFileLoadState.Loading;
        _openP4kTask = Task.Run(() =>
        {
            _p4KFile = P4kDirectoryNode.FromP4k(P4kFile.FromFile(path, p4kProgress), fileSystemProgress);
            // Chargement des données
            var entry = P4KFileSystem.OpenRead(dataCorePath);
            var dcb = new DataCoreDatabase(entry);
            df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
            entry.Dispose();
        }, _cancellationTokenSource.Token)
            // Une fois ouvert on supprime la task
            .ContinueWith(t =>
            {
                _openP4kTask = null;
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException() ?? t.Exception!;
                    _lastErrorMessage = ex.Message;
                    FileLoadState = P4kFileLoadState.Error;
                    throw ex;
                }
                else if (t.IsCanceled)
                {
                    FileLoadState = P4kFileLoadState.Cancelled;
                }
                else
                {
                    FileLoadState = P4kFileLoadState.Loaded;
                }
            });
        
        
        return _openP4kTask;
    }
    
    public async Task<IList<P4kFileModel>> LoadDefaultP4kLocations()
    {
        var result = new List<P4kFileModel>(5);
        var pathList = TryGetListOfInstallDirectory();

        if (pathList.Count == 0)
        {
            pathList.Add(DefaultStarCitizenFolder);
        }

        foreach (var path in pathList)
        {
            var installations = await GetP4ksFromDirectoryAsync(path).ConfigureAwait(false);
            result.AddRange(installations);
        }
        
        return result;
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
    private List<string> TryGetListOfInstallDirectory()
    {
        var result = new List<string>(50);

        var launcherPath = DefaultRSILauncherFolder;
        if (!Directory.Exists(launcherPath))
        {
            _logger.LogError("Failed to find RSI Launcher directory");
            return result;
        }

        var logPath = Path.Combine(launcherPath, "logs", "log.log");

        if (!File.Exists(logPath))
        {
            _logger.LogError("Failed to find RSI Launcher log");
            return result;
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
                var dir = installDirectory;
                
                result.Add(dir);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse SC install directory from launcher log");
            }
        }

        // On prend uniquement les chemins distincts
        result = result.Distinct().ToList();
        _logger.LogInformation($"{result.Count} chemins trouvés");
        return result;
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
            // Démarrage du chargement de cache (locale)
            FileLoadState = P4kFileLoadState.CacheLoading;
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

            // Mise à jour de l'état de cache à la fin
            _loadingLocalTask.ContinueWith(t =>
            {
                UpdateCacheStateFromTasks();
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException() ?? t.Exception!;
                    _lastErrorMessage = ex.Message;
                    FileLoadState = P4kFileLoadState.Error;
                }
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
        
        await LoadLangFileIfNeeded().ConfigureAwait(false);

        return _locale.GetValueOrDefault(key, key);
    }

    private Task LoadDatabaseIfNeeded()
    {
        if (null == _loadingDatabaseTask)
        {
            // Démarrage du chargement de cache (database)
            FileLoadState = P4kFileLoadState.CacheLoading;
            _loadingDatabaseTask = Task.Run(async () =>
            {
                // chargement du fichier p4k
                await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
                // Chargement des données
                // var entry = P4KFileSystem.OpenRead(dataCorePath);
                // var dcb = new DataCoreDatabase(entry);
                // var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
                // await entry.DisposeAsync().ConfigureAwait(false);

                var sw = Stopwatch.StartNew();
                var allRecords = df.DataCore.Database.MainRecords
                    .AsParallel()
                    .Select(x =>
                    {
                        if (_cancellationTokenSource.IsCancellationRequested) return null;
                        var savedDepth = DataCoreBinaryGenerated.s_maxRecursiveLoad;
                        // Pas de chargement récursif pour le chargement initial
                        DataCoreBinaryGenerated.s_maxRecursiveLoad = 0;
                        try
                        {
                            var result = df.GetFromRecord(x);
                            
                            return result;
                        }
                        finally
                        {
                            DataCoreBinaryGenerated.s_maxRecursiveLoad = savedDepth;
                        }
                    })
                    ;
                sw.Stop();
                _logger.LogTrace("Extracted all records in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);

                sw = Stopwatch.StartNew();
                foreach (var record in allRecords.Where(r => r?.Data is EntityClassDefinition or StarMapObject or ContractGenerator or CraftingBlueprintRecord or BlueprintCategoryDatabaseRecord))
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        // une demande d'annulation est arrivée
                        break;
                    }
                    
                    var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record!.RecordId]));
                    var cacheEntry = new CacheEntry
                    {
                        depth = 0,
                        Record = record
                    };
                    _EntityClassDict.Add(crc, cacheEntry);
                    _entityClassGuidDict.Add(record.RecordId, cacheEntry);
                }
                sw.Stop();
                _logger.LogTrace("Extracted all entity classes in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
            }, _cancellationTokenSource.Token);

            // Mise à jour de l'état de cache à la fin
            _loadingDatabaseTask.ContinueWith(t =>
            {
                UpdateCacheStateFromTasks();
                if (t.IsFaulted)
                {
                    var ex = t.Exception?.GetBaseException() ?? t.Exception!;
                    _lastErrorMessage = ex.Message;
                    FileLoadState = P4kFileLoadState.Error;
                }
            });
        }
        
        return _loadingDatabaseTask;        
    }

    public Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc)
    {
        return GetEntityType(guidCrc, 0);
    }

    public async Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc, int depth)
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);
        
        // TODO traiter correctement la notion de depth si <>
        var cacheEntry = _EntityClassDict.GetValueOrDefault(guidCrc);

        if (null != cacheEntry && cacheEntry.depth < depth)
        {
            await UpdateCacheRecordWithDepth(cacheEntry, depth).ConfigureAwait(false);
        }
        
        return cacheEntry?.Record;
    }

    public async IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinition(int depth)
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        var it = _EntityClassDict.Values
            .Where(r => r.Record.Data is EntityClassDefinition)
            .AsParallel()
            .Select(r =>
            {
                UpdateCacheRecordWithDepth(r, depth).Wait();
                return r.Record;
            });

        foreach (var record in it)
        {
            yield return record;       
        }
        
        // foreach (var record in _EntityClassDict.Values)
        // {
        //     if (record.Record.Data is EntityClassDefinition)
        //     {
        //         if (record.depth < depth)
        //         {
        //             await UpdateCacheRecordWithDepth(record, depth).ConfigureAwait(false);
        //         }
        //         
        //         yield return record.Record;
        //     }
        // }
    }

    public Task FillDataCache()
    {
        FileLoadState = P4kFileLoadState.CacheLoading;
        var task1 = LoadDatabaseIfNeeded();
        var task2 = LoadLangFileIfNeeded();
        var all = Task.WhenAll(task1, task2);

        all.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                var ex = t.Exception?.GetBaseException() ?? t.Exception!;
                _lastErrorMessage = ex.Message;
                FileLoadState = P4kFileLoadState.Error;
            }
            else if (t.IsCanceled)
            {
                FileLoadState = P4kFileLoadState.Cancelled;
            }
            else
            {
                FileLoadState = P4kFileLoadState.CacheLoaded;
            }
        });
        
        return all;
    }

    public async Task<List<DataCoreTypedRecord>> GetAllContractGenerator()
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        var result = new List<DataCoreTypedRecord>(50);

        foreach (var record in _EntityClassDict.Values)
        {
            if (record.Record.Data is ContractGenerator)
            {
                result.Add(record.Record);
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
        // var entry = P4KFileSystem.OpenRead(dataCorePath);
        // var dcb = new DataCoreDatabase(entry);
        // var df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
        // await entry.DisposeAsync().ConfigureAwait(false);

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

    public async Task<DataCoreTypedRecord?> GetRecordWithSpecificDepth(CigGuid recordId, int depth)
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);
        _entityClassGuidDict.TryGetValue(recordId, out var cacheEntry);

        if (null != cacheEntry && cacheEntry.depth < depth)
        {
            await UpdateCacheRecordWithDepth(cacheEntry, depth).ConfigureAwait(false);
        }

        return cacheEntry?.Record;
    }

    private void ResetSelectedFile()
    {
        // stop previous loading if any
        _cancellationTokenSource.Cancel();
        
        // clear caches
        _EntityClassDict.Clear();
        _entityClassGuidDict.Clear();
        _locale.Clear();
        _loadingLocalTask = null;
        _loadingDatabaseTask = null;
        
        
        // reset file
        _p4KFile = null;
        _lastErrorMessage = null;
        FileLoadState = P4kFileLoadState.NotLoaded;
    }

    private void UpdateCacheStateFromTasks()
    {
        // Si déjà en erreur, ne pas surcharger l'état
        if (FileLoadState == P4kFileLoadState.Error) return;

        bool anyStarted = _loadingLocalTask != null || _loadingDatabaseTask != null;
        bool anyFaulted = (_loadingLocalTask?.IsFaulted ?? false) || (_loadingDatabaseTask?.IsFaulted ?? false);
        bool anyRunning = (_loadingLocalTask != null && !_loadingLocalTask.IsCompleted) || (_loadingDatabaseTask != null && !_loadingDatabaseTask.IsCompleted);
        bool allCompletedForStarted = (_loadingLocalTask == null || _loadingLocalTask.IsCompletedSuccessfully)
                                      && (_loadingDatabaseTask == null || _loadingDatabaseTask.IsCompletedSuccessfully);

        if (anyFaulted)
        {
            var ex = _loadingLocalTask?.Exception?.GetBaseException() ?? _loadingDatabaseTask?.Exception?.GetBaseException();
            _lastErrorMessage = ex?.Message;
            FileLoadState = P4kFileLoadState.Error;
        }
        else if (anyRunning)
        {
            FileLoadState = P4kFileLoadState.CacheLoading;
        }
        else if (anyStarted && allCompletedForStarted)
        {
            FileLoadState = P4kFileLoadState.CacheLoaded;
        }
    }

    private async Task UpdateCacheRecordWithDepth(CacheEntry cacheEntry, int newDepth)
    {
        if (newDepth <= cacheEntry.depth)
        {
            // On est déjà avec plus d'infos, on ne fait rien
            return;
        }
        
        // chargement du fichier p4k
        await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
        // Chargement des données
        // var entry = P4KFileSystem.OpenRead(dataCorePath);
        // var dcb = new DataCoreDatabase(entry);
        // df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
        // await entry.DisposeAsync().ConfigureAwait(false);

        var oldval = DataCoreBinaryGenerated.s_maxRecursiveLoad;
        try
        {
            DataCoreBinaryGenerated.s_maxRecursiveLoad = newDepth;
            var record = df.GetFromRecord(cacheEntry.Record.RecordId);
            
            cacheEntry.Record = record;
            cacheEntry.depth = newDepth;
        }
        finally
        {
            DataCoreBinaryGenerated.s_maxRecursiveLoad = oldval;
        }
    }

    private class CacheEntry
    {
        public required int depth { get; set; }
        public required DataCoreTypedRecord Record { get; set; }
    }
}