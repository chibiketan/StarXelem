using StarBreaker.DataCoreGenerated;

namespace StarXelem.Services;

public record EntityType(EItemType type, EItemSubType subtype);

public interface IEntityClassDefinitionService
{
    EntityType GetType(EntityClassDefinition? entityClass);
}