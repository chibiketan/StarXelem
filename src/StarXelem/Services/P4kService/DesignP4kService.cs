using StarBreaker.DataCoreGenerated;
using StarBreaker.FileSystem;
using StarBreaker.P4k;
using StarXelem.Models;

namespace StarXelem.Services;

public class DesignP4kService : IP4kService
{
    private P4kFileModel? _selectedP4KFile;
    public P4kDirectoryNode P4KFileSystem { get; }

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

    public async IAsyncEnumerable<DataCoreTypedRecord> GetAllEntityClassDefinition()
    {
        yield break;
    }

    private static P4kEntry[] GetFakeEntries() =>
    [
        new(@"Data\entry1", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Data\entry2", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Data\ObjectContainers\entry2", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Data\Textures\entry2", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Engine\entry3", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef),
        new(@"Engine\entry3", 69, 69, 0, false, 123, 0xffff, 0xdeadbeef)
    ];
}