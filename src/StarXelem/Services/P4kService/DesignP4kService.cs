using System.ComponentModel;
using StarBreaker.Common;
using StarBreaker.DataCoreGenerated;
using StarBreaker.Extraction;
using StarBreaker.FileSystem;
using StarBreaker.P4k;
using StarXelem.Models;

namespace StarXelem.Services;

public class DesignP4kService : IP4kService
{
    private P4kFileModel? _selectedP4KFile;
    public P4kDirectoryNode P4KFileSystem { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    // Valeurs design-time pour l'aperçu
    public P4kService.P4kFileLoadState FileLoadState { get; private set; } = P4kService.P4kFileLoadState.CacheLoaded;
    public string? GetLastErrorMessage() => null;

    public P4kFileModel? SelectedP4KFile
    {
        get => _selectedP4KFile;
        set
        {
            _selectedP4KFile = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedP4KFile)));
            SelectedP4KFileChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<P4kFileModel?>? SelectedP4KFileChanged;
    
    public DesignP4kService()
    {
        var entries = GetFakeEntries();

        P4KFileSystem = P4kDirectoryNode.FromP4k(new FakeP4kFile(@"C:\This\Is\A\Path", entries));
    }

    public Task OpenP4k(string path, IProgress<double> p4kProgress, IProgress<double> fileSystemProgress)
    {
        p4kProgress.Report(0);
        Thread.Sleep(100);
        p4kProgress.Report(0.5);
        Thread.Sleep(100);
        p4kProgress.Report(1);
        Thread.Sleep(100);
        fileSystemProgress.Report(0);
        Thread.Sleep(100);
        fileSystemProgress.Report(0.5);
        Thread.Sleep(100);
        fileSystemProgress.Report(1);
        return Task.FromResult<P4kDirectoryNode>(null!);
    }

    public Task<IList<P4kFileModel>> FindInstalledFiles()
    {
        return Task.FromResult<IList<P4kFileModel>>(new List<P4kFileModel>
        {
            new()
            {
                ChannelName = "TestChannel 1",
                Path = "un/chemin/de/test 1",
                Manifest = new BuildManifestModel
                {
                    Data = new BuildManifestDataModel
                    {
                        Branch = "TestBranch",
                        Config = "Config 1",
                        Platform = "PC",
                        Version = "1.0.0.0"
                    }
                }
            }
        });
    }

    public Task<P4kFileModel?> GetInstallationInfo(string p4kPath)
    {
        return Task.FromResult(new P4kFileModel
        {
            ChannelName = "TestChannel 1",
            Path = "un/chemin/de/test 1",
            Manifest = new BuildManifestModel
            {
                Data = new BuildManifestDataModel
                {
                    Branch = "TestBranch",
                    Config = "Config 1",
                    Platform = "PC",
                    Version = "1.0.0.0"
                }
            }
        })!;
    }

    public Task<string?> GetLocaleValue(string? key)
    {
        return Task.FromResult(key);
    }

    public Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc)
    {
        return Task.FromResult<DataCoreTypedRecord?>(null);
    }

    public Task<DataCoreTypedRecord?> GetEntityType(uint guidCrc, int depth)
    {
        return Task.FromResult<DataCoreTypedRecord?>(null);
    }

    public async IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinition(int depth)
    {
        yield break;
    }

    public async IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinitionFiltered(
        int filterDepth, int finalDepth, Func<EntityClassDefinition, bool> predicate)
    {
        yield break;
    }

    public async IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinitionFilteredBatched(
        int filterDepth, int finalDepth, Func<EntityClassDefinition, bool> predicate, int batchSize)
    {
        yield break;
    }

    public Task FillDataCache()
    {
        return Task.CompletedTask;
    }

    public Task<List<DataCoreTypedRecord>> GetAllContractGenerator()
    {
        return Task.FromResult(new List<DataCoreTypedRecord>());
    }
    
    public Task<string?> GetEntityClassName(EntityClassDefinition? entityClass)
    {
        return Task.FromResult("GetEntityClassName")!;
    }
    
    public Task<DataCoreTypedRecord> GetRecordWithFullHistory(CigGuid recordId)
    {
        return Task.FromResult(new DataCoreTypedRecord("toto", "toto", default, null));
    }
    
    public Task<TagDatabase> GetTagDatabase()
    {
        return Task.FromResult(new TagDatabase
        {
            selfId = default,
            tags = []
        });
    }
    
    public Task<DataCoreTypedRecord?> GetRecordWithSpecificDepth(CigGuid recordId, int depth)
    {
        return Task.FromResult(new DataCoreTypedRecord("toto", "toto", default, null));   
    }
    
    public Task<List<DataCoreTypedRecord>> EnsureRecordsDepthAsync(IEnumerable<DataCoreTypedRecord> records, int depth)
    {
        return Task.FromResult(new List<DataCoreTypedRecord>());
    }
    
    public Task<List<DataCoreTypedRecord>> GetAllFactionReputations()
    {
        return Task.FromResult(new List<DataCoreTypedRecord>());  
    }

    public Task<List<DataCoreTypedRecord>> GetAllCraftingBlueprintRecord()
    {
        return Task.FromResult(new List<DataCoreTypedRecord>());
    }

    public Task<List<DataCoreTypedRecord>> GetAllStarMapObjects()
    {
        return Task.FromResult(new List<DataCoreTypedRecord>());
    }

    public Task<object?> GetRecordById(CigGuid recordId)
    {
        return Task.FromResult<object?>(null);
    }

    public void ReleaseHeavyCache()
    {
    }

    private static P4kEntry[] GetFakeEntries() =>
    [
        new(@"Data\entry1", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Data\entry2", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Data\ObjectContainers\entry2", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Data\Textures\entry2", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Engine\entry3", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef)
    ];
}