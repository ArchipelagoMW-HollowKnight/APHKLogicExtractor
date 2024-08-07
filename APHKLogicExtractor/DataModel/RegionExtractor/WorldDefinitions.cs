﻿using APHKLogicExtractor.DataModel.RegionExtractor;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RandomizerCore.Logic;

namespace APHKLogicExtractor.DataModel
{
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum LogicHandling
    {
        Default,
        Location,
        Transition
    }

    internal record LogicObjectDefinition(
        string Name,
        IEnumerable<StatefulClause> Logic,
        LogicHandling Handling = LogicHandling.Default,
        bool IsEventLocation = false);

    internal record StringWorldDefinition(IEnumerable<LogicObjectDefinition> LogicObjects, LogicManager? BackingLm = null);

    internal record GraphWorldDefinition(
        IEnumerable<Region> Regions,
        IEnumerable<GraphLocation> Locations,
        IEnumerable<RandomizableTransition> Transitions,
        IDictionary<string, string> TransitionToRegionMap);
}
