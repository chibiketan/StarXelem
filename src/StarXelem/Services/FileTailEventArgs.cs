namespace StarXelem.Services;

public class FileTailEventArgs : EventArgs
{
    public string Line { get; }

    public FileTailEventArgs(string line) => Line = line;
}
