using APHKLogicExtractor.Loader;
using Newtonsoft.Json;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringItems;

namespace APHKLogicExtractor.DataModel;

internal record JsonLogicConfiguration
{
    [JsonProperty]
    internal Data? data;
    [JsonProperty]
    internal Logic? logic;

    internal record Data
    {
        [JsonProperty]
        internal MaybeFile<Dictionary<string, RoomDef>>? rooms;
        [JsonProperty]
        internal MaybeFile<Dictionary<string, LocationDef>>? locations;
        [JsonProperty]
        internal MaybeFile<Dictionary<string, TransitionDef>>? transitions;
    }

    internal record Logic
    {
        [JsonProperty]
        internal MaybeFile<Dictionary<string, List<string>>>? terms;
        [JsonProperty]
        internal MaybeFile<RawStateData>? state;
        [JsonProperty]
        internal MaybeFile<Dictionary<string, string>>? macros;
        [JsonProperty]
        internal MaybeFile<List<RawLogicDef>>? transitions;
        [JsonProperty]
        internal MaybeFile<List<RawLogicDef>>? locations;
        [JsonProperty]
        internal MaybeFile<List<RawWaypointDef>>? waypoints;
        [JsonProperty]
        internal MaybeFile<List<StringItemTemplate>>? items;
    }
}
