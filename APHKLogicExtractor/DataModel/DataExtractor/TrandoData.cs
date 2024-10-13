using APHKLogicExtractor.DataModel.RandomizerData;

namespace APHKLogicExtractor.DataModel.DataExtractor
{
    internal record TransitionDetails(
        string VanillaTarget,
        Direction Direction,
        Sidedness Sides,
        string MapArea,
        bool IsMapAreaTransition,
        string TitledArea,
        bool IsTitledAreaTransition
    );

    internal record StartDetails(string LogicName, string GrantedTransition, List<RequirementBranch> Logic);

    internal record TrandoData(Dictionary<string, TransitionDetails> Transitions, Dictionary<string, StartDetails> Starts);
}
