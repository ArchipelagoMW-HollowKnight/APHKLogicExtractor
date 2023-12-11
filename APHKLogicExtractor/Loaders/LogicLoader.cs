using RandomizerCore.Logic;

namespace APHKLogicExtractor.Loaders
{
    internal class LogicLoader(string refName) : BaseLoader(refName)
    {
        public async Task<Dictionary<string, string>> LoadMacros() => await LoadJsonCached<Dictionary<string, string>>(
            "RandomizerMod/Resources/Logic/macros.json");

        public async Task<List<RawLogicDef>> LoadTransitions() => await LoadJsonCached<List<RawLogicDef>>(
            "RandomizerMod/Resources/Logic/transitions.json");

        public async Task<List<RawLogicDef>> LoadLocations() => await LoadJsonCached<List<RawLogicDef>>(
            "RandomizerMod/Resources/Logic/locations.json");

        public async Task<List<RawWaypointDef>> LoadWaypoints() => await LoadJsonCached<List<RawWaypointDef>>(
            "RandomizerMod/Resources/Logic/waypoints.json");
    }
}
