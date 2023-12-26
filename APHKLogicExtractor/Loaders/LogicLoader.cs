using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;

namespace APHKLogicExtractor.Loaders
{
    internal class LogicLoader(string refName) : BaseLoader(refName)
    {
        public async Task<TermCollectionBuilder> LoadTerms()
        {
            Dictionary<string, List<string>> terms = await LoadJsonCached<Dictionary<string, List<string>>>(
                "RandomizerMod/Resources/Logic/terms.json");
            TermCollectionBuilder termsBuilder = new();
            foreach (var (type, termsOfType) in terms)
            {
                TermType termType = (TermType)Enum.Parse(typeof(TermType), type);
                foreach (string term in termsOfType)
                {
                    termsBuilder.GetOrAddTerm(term, termType);
                }
            }
            return termsBuilder;
        }

        public async Task<RawStateData> LoadStateFields() => await LoadJsonCached<RawStateData>(
            "RandomizerMod/Resources/Logic/state.json");

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
