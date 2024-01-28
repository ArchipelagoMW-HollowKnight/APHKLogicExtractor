namespace APHKLogicExtractor.DataModel
{
    internal enum LogicHandling
    {
        Default,
        Location,
        Transition
    }

    internal record LogicObjectDefinition(string Name, IEnumerable<StatefulClause> Logic, LogicHandling Handling = LogicHandling.Default);

    internal record StringWorldDefinition(IEnumerable<LogicObjectDefinition> StateTransmitters, IEnumerable<LogicObjectDefinition> Locations);

    internal record GraphWorldDefinition(IEnumerable<Region> Regions, IEnumerable<GraphLocation> Locations);
}
