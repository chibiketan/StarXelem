using Microsoft.EntityFrameworkCore;

namespace StarXelem.Data;

public interface IScItemRepository
{
    Task<ScItemEntity?> GetByCrc32Async(uint crc32);
}
