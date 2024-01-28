using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace APHKLogicExtractor.DataModel
{
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum LogicHandling
    {
        Default,
        Location,
        Transition
    }

    internal record LogicObjectDefinition(string Name, IEnumerable<StatefulClause> Logic, LogicHandling Handling = LogicHandling.Default);

    internal record StringWorldDefinition(IEnumerable<LogicObjectDefinition> LogicObjects);

    internal record GraphWorldDefinition(IEnumerable<Region> Regions, IEnumerable<GraphLocation> Locations);
}
