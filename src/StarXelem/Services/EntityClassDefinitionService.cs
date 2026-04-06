using StarBreaker.DataCoreGenerated;

namespace StarXelem.Services;

public class EntityClassDefinitionService : IEntityClassDefinitionService
{
    public EntityType GetType(EntityClassDefinition? entityClass)
    {
        var attachable = entityClass?.Components.OfType<SAttachableComponentParams>().FirstOrDefault();
        var type = attachable?.AttachDef.Type ?? EItemType.__Unknown;
        var subtype = attachable?.AttachDef.SubType ?? EItemSubType.__Unknown;
        return new EntityType(type, subtype);
    }
}