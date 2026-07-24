using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StarBreaker.Common;
using StarBreaker.DataCore;
using StarBreaker.DataCoreGenerated;
using StarBreaker.Extraction;
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
    private DataForge<DataCoreTypedRecord> df;
    private string? _lastErrorMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public enum P4kFileLoadState
    {
        NotLoaded = 0,
        Cancelled,
        Error,
        Loading,
        Loaded,
        CacheLoading,
        CacheLoaded
    }

    private P4kFileLoadState _fileLoadState = P4kFileLoadState.NotLoaded;

    public P4kFileLoadState FileLoadState
    {
        get => _fileLoadState;
        private set => UpdateState(value);
    }

    public string? GetLastErrorMessage()
    {
        return _fileLoadState == P4kFileLoadState.Error ? _lastErrorMessage : null;
    }

    /// <summary>
    /// Transition vers un nouvel état — ignore les transitions redondantes.
    /// </summary>
    private void UpdateState(P4kFileLoadState newState)
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

    public async Task OpenP4k(string path, IProgress<double> p4kProgress, IProgress<double> fileSystemProgress)
    {
        if (_p4KFile != null)
        {
            // _logger.LogWarning("P4k file already open");
            if ((int)FileLoadState < (int)P4kFileLoadState.Loaded)
            {
                UpdateState(P4kFileLoadState.Loaded);
            }
            return;
        }
        
        // On réinitialise la source de token vu que c'est un nouveau fichier
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        _lastErrorMessage = null;
        if ((int)FileLoadState < (int)P4kFileLoadState.Loading)
            UpdateState(P4kFileLoadState.Loading);

        try
        {
            await Task.Run(() =>
            {
                _p4KFile = P4kDirectoryNode.FromP4k(P4kFile.FromFile(path, p4kProgress), null, fileSystemProgress);
                // Chargement des donnees
                var entry = P4KFileSystem.OpenRead(dataCorePath);
                var dcb = new DataCoreDatabase(entry);
                df = new DataForge<DataCoreTypedRecord>(new DataCoreBinaryGenerated(dcb));
                entry.Dispose();
            }, _cancellationTokenSource.Token).ConfigureAwait(false);
            if ((int)FileLoadState < (int)P4kFileLoadState.Loaded)
                UpdateState(P4kFileLoadState.Loaded);
        }
        catch (OperationCanceledException)
        {
            UpdateState(P4kFileLoadState.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            _lastErrorMessage = ex.Message;
            UpdateState(P4kFileLoadState.Error);
            throw;
        }
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

    private async Task LoadLangFileIfNeeded()
    {
        if (_loadingLocalTask != null)
        {
            await _loadingLocalTask.ConfigureAwait(false);
            return;
        }

        // On crée une source que l'on pourra utiliser pour remonter la tâche comme chargée
        var tcs = new TaskCompletionSource();
        _loadingLocalTask = tcs.Task;

        // Démarrage du chargement de cache (locale)
        UpdateState(P4kFileLoadState.CacheLoading);

        try
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

                    if (!string.IsNullOrEmpty(line))
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
        }
        catch (OperationCanceledException)
        {
            UpdateState(P4kFileLoadState.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            _lastErrorMessage = ex.Message;
            UpdateState(P4kFileLoadState.Error);
            throw;
        }
        finally
        {
            tcs.SetResult();
            UpdateCacheStateFromTasks();
        }
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

    private async Task LoadDatabaseIfNeeded()
    {
        if (_loadingDatabaseTask != null)
        {
            await _loadingDatabaseTask.ConfigureAwait(false);
            return;
        }

        // On crée une source que l'on pourra utiliser pour remonter la tâche comme chargée
        var tcs = new TaskCompletionSource();
        _loadingDatabaseTask = tcs.Task;

        // Démarrage du chargement de cache (database)
        UpdateState(P4kFileLoadState.CacheLoading);

        try
        {
            // chargement du fichier p4k
            await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);
            // Chargement des données

            var sw = Stopwatch.StartNew();
            var allRecords = df.DataCore.Database.RecordDefinitions.AsParallel()
                .Select(record =>
                {
                    // On initialise uniquement les données vides, comme ça on a juste la structure sans la charge du traitement
                    return df.DataCore.GetEmptyRecord(record);
                });
            sw.Stop();
            _logger.LogTrace("Extracted all records in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);

            sw = Stopwatch.StartNew();
            foreach (var record in allRecords)
            {
                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    // une demande d'annulation est arrivée
                    break;
                }

                var crc = Crc32c.FromSpan(MemoryMarshal.Cast<CigGuid, byte>([record!.RecordId]));
                var cacheEntry = new CacheEntry
                {
                    depth = -1,
                    Record = record
                };
                _EntityClassDict.Add(crc, cacheEntry);
                _entityClassGuidDict.Add(record.RecordId, cacheEntry);
            }
            sw.Stop();
            _logger.LogTrace("Extracted all entity classes in {ElapsedMilliseconds}ms", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            UpdateState(P4kFileLoadState.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            _lastErrorMessage = ex.Message;
            UpdateState(P4kFileLoadState.Error);
            throw;
        }
        finally
        {
            tcs.SetResult();
            UpdateCacheStateFromTasks();
        }
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

        // Avec le as parallel
        var records = _EntityClassDict.Values
            .AsParallel()
            .Where(r => r.Record.Data is EntityClassDefinition)
            .ToList();
        
        await Task.WhenAll(records.AsParallel().Select(async r =>
        {
            if (r.depth < depth)
                await UpdateCacheRecordWithDepth(r, depth);
        })).ConfigureAwait(false);
        
        foreach (var record in records)
        {
            yield return record.Record;
        }
    }

    /// <summary>
    /// Comme <see cref="GetAllEntityClassDefinition"/>, mais évite d'étendre TOUS les EntityClassDefinition
    /// du jeu jusqu'à <paramref name="finalDepth"/> : le prédicat est d'abord évalué à une profondeur plus
    /// légère (<paramref name="filterDepth"/>), et seuls les enregistrements retenus sont ensuite étendus
    /// jusqu'à <paramref name="finalDepth"/>. Réduit fortement le nombre d'objets lourdement matérialisés
    /// quand seule une fraction des entités du jeu est réellement utile à l'appelant (ex: SCItems parmi
    /// vaisseaux/PNJ/props).
    /// </summary>
    public async IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinitionFiltered(
        int filterDepth,
        int finalDepth,
        Func<EntityClassDefinition, bool> predicate)
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        var records = _EntityClassDict.Values
            .AsParallel()
            .Where(r => r.Record.Data is EntityClassDefinition)
            .ToList();

        await Task.WhenAll(records.AsParallel().Select(async r =>
        {
            if (r.depth < filterDepth)
                await UpdateCacheRecordWithDepth(r, filterDepth);
        })).ConfigureAwait(false);

        var matching = records
            .Where(r => r.Record.Data is EntityClassDefinition ec && predicate(ec))
            .ToList();

        await Task.WhenAll(matching.AsParallel().Select(async r =>
        {
            if (r.depth < finalDepth)
                await UpdateCacheRecordWithDepth(r, finalDepth);
        })).ConfigureAwait(false);

        foreach (var record in matching)
        {
            yield return record.Record;
        }
    }

    public async Task FillDataCache()
    {
        UpdateState(P4kFileLoadState.CacheLoading);
        var task1 = LoadDatabaseIfNeeded();
        var task2 = LoadLangFileIfNeeded();

        try
        {
            await Task.WhenAll(task1, task2).ConfigureAwait(false);
            UpdateState(P4kFileLoadState.CacheLoaded);
        }
        catch (OperationCanceledException)
        {
            UpdateState(P4kFileLoadState.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            _lastErrorMessage = ex.Message;
            UpdateState(P4kFileLoadState.Error);
            throw;
        }
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

    /// <inheritdoc/>
    public async Task<TagDatabase> GetTagDatabase()
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);
        var cacheEntry = _EntityClassDict.Values.First(r => r.Record.Data is TagDatabase);
        
        // TODO Crash à 10, comment augmenter cette limite ???
        await UpdateCacheRecordWithDepth(cacheEntry, 15).ConfigureAwait(false);
        return (TagDatabase)cacheEntry.Record.Data;
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

    public async Task<List<DataCoreTypedRecord>> GetAllFactionReputations()
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        var targetDepth = 4;
        var results = _EntityClassDict.Values
            .Where(r => r.Record.Data is FactionReputation)
            .ToList();

        // On met à jour les entrées si besoin
        await Task.WhenAll(results
            .AsParallel()
            .Select(async p =>
            {
                if (p.depth < targetDepth)
                {
                    await UpdateCacheRecordWithDepth(p, targetDepth).ConfigureAwait(false);
                }
            }));

        return results.Select(r => r.Record).ToList();
    }

    public async Task<List<DataCoreTypedRecord>> GetAllCraftingBlueprintRecord()
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        var result = new List<DataCoreTypedRecord>(500);

        foreach (var record in _EntityClassDict.Values)
        {
            if (record.Record.Data is CraftingBlueprintRecord)
            {
                result.Add(record.Record);
            }
        }

        return result;
    }

    public async Task<List<DataCoreTypedRecord>> GetAllStarMapObjects()
    {
        await LoadDatabaseIfNeeded().ConfigureAwait(false);

        var records = _EntityClassDict.Values
            .AsParallel()
            .Where(r => r.Record.Data is StarMapObject)
            .ToList();

        // StarMapObject properties (name, type, parent, etc.) are at depth 1
        await Task.WhenAll(records.AsParallel().Select(async r =>
        {
            if (r.depth < 1)
                await UpdateCacheRecordWithDepth(r, 1);
        })).ConfigureAwait(false);

        return records.Select(r => r.Record).ToList();
    }

    /// <summary>
    /// Récupère un enregistrement arbitraire par son CigGuid via le DataForge.
    /// Utile pour charger des records qui ne sont pas des EntityClassDefinition (ResourceType, MineableComposition, etc.)
    /// </summary>
    public void ReleaseHeavyCache()
    {
        var count = _EntityClassDict.Count;
        _EntityClassDict.Clear();
        _entityClassGuidDict.Clear();
        // Permet à LoadDatabaseIfNeeded() de reconstruire le cache (à profondeur minimale, donc bon marché)
        // la prochaine fois qu'il sera nécessaire, au lieu de le considérer comme déjà chargé.
        _loadingDatabaseTask = null;
        _logger.LogInformation("Cache lourd P4kService libéré ({Count} enregistrements).", count);
    }

    public async Task<object?> GetRecordById(CigGuid recordId)
    {
        await OpenP4k(SelectedP4KFile.Path, new Progress<double>(), new Progress<double>()).ConfigureAwait(false);

        return await LargeStackThreadPool.Shared.EnqueueAsync(() =>
        {
            var oldval = DataCoreBinaryGenerated.s_maxRecursiveLoad;
            try
            {
                DataCoreBinaryGenerated.s_maxRecursiveLoad = 5;
                return df.GetFromRecord(recordId)?.Data;
            }
            finally
            {
                DataCoreBinaryGenerated.s_maxRecursiveLoad = oldval;
            }
        }).ConfigureAwait(false);
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
        UpdateState(P4kFileLoadState.NotLoaded);
    }

    private void UpdateCacheStateFromTasks()
    {
        // Guard: never downgrade from Error or CacheLoaded
        if (FileLoadState == P4kFileLoadState.Error || FileLoadState == P4kFileLoadState.CacheLoaded)
            return;

        bool anyStarted = _loadingLocalTask != null || _loadingDatabaseTask != null;
        bool anyFaulted = (_loadingLocalTask?.IsFaulted ?? false) || (_loadingDatabaseTask?.IsFaulted ?? false);
        bool anyRunning = (_loadingLocalTask != null && !_loadingLocalTask.IsCompleted) || (_loadingDatabaseTask != null && !_loadingDatabaseTask.IsCompleted);
        bool allCompletedForStarted = (_loadingLocalTask == null || _loadingLocalTask.IsCompletedSuccessfully)
                                      && (_loadingDatabaseTask == null || _loadingDatabaseTask.IsCompletedSuccessfully);

        if (anyFaulted)
        {
            var ex = _loadingLocalTask?.Exception?.GetBaseException() ?? _loadingDatabaseTask?.Exception?.GetBaseException();
            _lastErrorMessage = ex?.Message;
            UpdateState(P4kFileLoadState.Error);
        }
        else if (anyRunning)
        {
            UpdateState(P4kFileLoadState.CacheLoading);
        }
        else if (anyStarted && allCompletedForStarted)
        {
            UpdateState(P4kFileLoadState.CacheLoaded);
        }
    }

    public async Task<List<DataCoreTypedRecord>> EnsureRecordsDepthAsync(IEnumerable<DataCoreTypedRecord> records, int depth)
    {
        var tasks = records.AsParallel().Select(async record =>
        {
            if (_entityClassGuidDict.TryGetValue(record.RecordId, out var cacheEntry))
            {
                await UpdateCacheRecordWithDepth(cacheEntry, depth).ConfigureAwait(false);

                return cacheEntry.Record;
            }

            return record;
        })
        .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return tasks.Select(t => t.Result).ToList();
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

        UpdateState(P4kFileLoadState.CacheLoading);
        // Tentative d'exécution dans un thread modifiée pour avoir une grosse Stacktrace
        await LargeStackThreadPool.Shared.EnqueueAsync(() =>
        //await LargeStackRunner.RunAsync(() =>
        {
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
        });

        // After an individual record refresh, ensure we stay in CacheLoaded state.
        // This prevents the UI indicator from showing wrong state after depth updates.
        if (FileLoadState is P4kFileLoadState.CacheLoaded or P4kFileLoadState.CacheLoading)
        {
            UpdateState(P4kFileLoadState.CacheLoaded);
        }
    }

    private class CacheEntry
    {
        public required int depth { get; set; }
        public required DataCoreTypedRecord Record { get; set; }
    }
}

public static class LargeStackRunner
{
    private const int DefaultStackSizeMb = 64;

    public static T Run<T>(Func<T> func, int stackSizeMb = DefaultStackSizeMb)
    {
        T result = default!;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try   { result = func(); }
            catch (Exception ex) { capturedException = ex; }
        }, stackSizeMb * 1024 * 1024);

        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (capturedException is not null)
            ExceptionDispatchInfo.Capture(capturedException).Throw();

        return result!;
    }
    
    public static Task<T> RunAsync<T>(Func<T> func, int stackSizeMb = DefaultStackSizeMb)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try   { tcs.SetResult(func()); }
            catch (OperationCanceledException ex) { tcs.SetCanceled(ex.CancellationToken); }
            catch (Exception ex)                  { tcs.SetException(ex); }
        }, stackSizeMb * 1024 * 1024);

        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    public static Task RunAsync(Action action, int stackSizeMb = DefaultStackSizeMb)
        => RunAsync<object?>(() => { action(); return null; }, stackSizeMb);

    public static void Run(Action action, int stackSizeMb = DefaultStackSizeMb)
        => Run<object?>(() => { action(); return null; }, stackSizeMb);
}

public sealed class LargeStackThreadPool : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread[] _threads;
    private readonly CancellationTokenSource _cts = new();

    public static readonly LargeStackThreadPool Shared = new();

    public LargeStackThreadPool(int threadCount = 10, int stackSizeMb = 64)
    {
        _threads = Enumerable.Range(0, threadCount)
            .Select(i =>
            {
                var t = new Thread(WorkLoop, stackSizeMb * 1024 * 1024)
                {
                    IsBackground = true,
                    Name = $"LargeStack-{i}"
                };
                t.Start();
                return t;
            })
            .ToArray();
    }

    // Boucle 100% synchrone — le travail reste sur CE thread, pas de fuite vers le ThreadPool
    private void WorkLoop()
    {
        try
        {
            foreach (var work in _queue.GetConsumingEnumerable(_cts.Token))
                work();
        }
        catch (OperationCanceledException) { }
    }

    public Task<T> EnqueueAsync<T>(Func<T> func, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (ct.IsCancellationRequested)
        {
            tcs.SetCanceled(ct);
            return tcs.Task;
        }

        try
        {
            _queue.Add(() =>
            {
                if (ct.IsCancellationRequested) { tcs.SetCanceled(ct); return; }

                try   { tcs.SetResult(func()); }
                catch (OperationCanceledException ex) { tcs.SetCanceled(ex.CancellationToken); }
                catch (Exception ex)                  { tcs.SetException(ex); }
            });
        }
        catch (InvalidOperationException) { tcs.SetCanceled(); } // pool disposed

        return tcs.Task;
    }

    public Task EnqueueAsync(Action action, CancellationToken ct = default)
        => EnqueueAsync<object?>(() => { action(); return null; }, ct);

    public void Dispose()
    {
        _cts.Cancel();
        _queue.CompleteAdding();
        foreach (var t in _threads)
            t.Join(TimeSpan.FromSeconds(5));
        _cts.Dispose();
        _queue.Dispose();
    }
}