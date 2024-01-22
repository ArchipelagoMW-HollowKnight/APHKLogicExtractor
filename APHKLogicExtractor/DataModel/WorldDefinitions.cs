using APHKLogicExtractor.ExtractorComponents.RegionExtractor;

namespace APHKLogicExtractor.DataModel
{
    internal record LogicObjectDefinition(string Name, IEnumerable<StatefulClause> Logic);

    internal record StringWorldDefinition(IEnumerable<LogicObjectDefinition> StateTransmitters, IEnumerable<LogicObjectDefinition> Locations);

    internal record GraphWorldDefinition(IEnumerable<Region> Regions);
}
