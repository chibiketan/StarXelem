using Sc.External.Services.Entitygraph.V1;

namespace StarXelem.Models;

public class EntityItemQueryResult
{
    public EntityNodeProperties? EntityNodeProperties { get; set; }
    public EntitySnapshot? EntitySnapshot { get; set; }
    public EntityEdge? EntityEdge { get; set; }
    public EntityClassProperties? EntityClassProperties { get; set; }
}
