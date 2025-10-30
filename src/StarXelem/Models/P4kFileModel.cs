namespace StarXelem.Models;

public class P4kFileModel
{
    public required string ChannelName { get; init; }
    public required string Path { get; init; }
    public BuildManifestModel? Manifest { get; set; }

    public string DisplayVersion => $"{ChannelName} - {Manifest?.Data?.Branch}-{Manifest?.Data?.RequestedP4ChangeNum}";

}