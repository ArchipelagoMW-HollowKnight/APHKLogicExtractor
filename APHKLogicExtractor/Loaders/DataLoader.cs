using APHKLogicExtractor.DataModel;

namespace APHKLogicExtractor.Loaders
{
    internal class DataLoader(string refName) : BaseLoader(refName)
    {
        public async Task<Dictionary<string, RoomDef>> LoadRooms() => await LoadJsonCached<Dictionary<string, RoomDef>>(
            "RandomizerMod/Resources/Data/rooms.json");

        public async Task<Dictionary<string, LocationDef>> LoadLocations() => await LoadJsonCached<Dictionary<string, LocationDef>>(
            "RandomizerMod/Resources/Data/locations.json");

        public async Task<Dictionary<string, TransitionDef>> LoadTransitions() => await LoadJsonCached<Dictionary<string, TransitionDef>>(
            "RandomizerMod/Resources/Data/transitions.json");
    }
}
