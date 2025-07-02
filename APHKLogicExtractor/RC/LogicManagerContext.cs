using APHKLogicExtractor.DataModel;
using RandomizerCore.Logic;
using RandomizerCore.StringItems;
using RandomizerCore.StringLogic;
using RandomizerCore.Logic.StateLogic;
using System.Collections.Generic;

namespace APHKLogicExtractor.RC;

internal record LogicManagerContext(
    LogicManager LogicManager,
    TermCollectionBuilder Terms,
    RawStateData StateData,
    List<RawLogicDef> TransitionLogic,
    List<RawLogicDef> LocationLogic,
    Dictionary<string, string> MacroLogic,
    List<RawWaypointDef> WaypointLogic,
    List<StringItemTemplate> ItemTemplates
);
