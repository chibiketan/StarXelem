using System.Collections.ObjectModel;

namespace StarXelem.ViewModels.Overlay;

public class SignatureOverlayViewModel : ViewModelBase
{
    public string Signature { get; }
    public ObservableCollection<string> Lines { get; } = new();

    public SignatureOverlayViewModel(string signature, IEnumerable<string> lines)
    {
        Signature = signature;
        foreach (var line in lines)
        {
            Lines.Add(line);
        }
    }
}
