namespace StarXelem.Data;

public interface ILocaleEntryRepository
{
    Task<string?> GetValueByKeyAsync(string key);
}
