using APHKLogicExtractor.DataModel.RandomizerData;

namespace APHKLogicExtractor.DataModel.DataExtractor
{
    internal record StartData(string LogicName, string GrantedTransition, List<RequirementBranch> Logic);

    internal record TrandoData(Dictionary<string, TransitionDef> Transitions, Dictionary<string, StartData> starts);
}
