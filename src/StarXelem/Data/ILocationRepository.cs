namespace StarXelem.Data;

public interface ILocationRepository
{
    Task<LocationEntity?> GetByCrcAsync(uint crc);
    Task<List<LocationEntity>> GetAllAsync();
}
