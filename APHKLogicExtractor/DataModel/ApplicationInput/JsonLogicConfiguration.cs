using APHKLogicExtractor.DataModel.RandomizerData;
using APHKLogicExtractor.Loader;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringItems;

namespace APHKLogicExtractor.DataModel;

internal record JsonLogicConfiguration
{
    public JsonData? Data { get; set; }
    public JsonLogic? Logic { get; set; }
}

internal record JsonData
{
    public MaybeFile<Dictionary<string, RoomDef>>? Rooms { get; set; }
    public MaybeFile<Dictionary<string, LocationDef>>? Locations { get; set; }
    public MaybeFile<Dictionary<string, TransitionDef>>? Transitions { get; set; }
    public MaybeFile<List<PoolDef>>? Pools { get; set; }
    public MaybeFile<Dictionary<string, CostDef>>? Costs { get; set; }
    public MaybeFile<Dictionary<string, string>>? LogicSettings { get; set; }
    public MaybeFile<Dictionary<string, StartDef>>? Starts { get; set; }
}

internal record JsonLogic
{
    public MaybeFile<Dictionary<string, List<string>>>? Terms { get; set; }
    public MaybeFile<RawStateData>? State { get; set; }
    public MaybeFile<Dictionary<string, string>>? Macros { get; set; }
    public MaybeFile<List<RawLogicDef>>? Transitions { get; set; }
    public MaybeFile<List<RawLogicDef>>? Locations { get; set; }
    public MaybeFile<List<RawWaypointDef>>? Waypoints { get; set; }
    public MaybeFile<List<StringItemTemplate>>? Items { get; set; }
}
