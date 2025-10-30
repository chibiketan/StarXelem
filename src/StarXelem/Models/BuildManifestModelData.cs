namespace StarXelem.Models;

public class BuildManifestDataModel
{
    public string? Branch { get; set; }
    public string? BuildDateStamp { get; set; }
    public string? BuildId { get; set; }
    public string? BuildTimeStamp { get; set; }
    public string? Config { get; set; }
    public string? Platform { get; set; }
    public string? RequestedP4ChangeNum { get; set; }
    public string? Shelved_Change { get; set; }
    public string? Tag { get; set; }
    public string? Version { get; set; }
}