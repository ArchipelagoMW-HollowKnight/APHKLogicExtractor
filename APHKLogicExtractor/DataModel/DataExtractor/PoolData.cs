using APHKLogicExtractor.DataModel.RandomizerData;

namespace APHKLogicExtractor.DataModel.DataExtractor
{
    internal record PoolData(Dictionary<string, List<VanillaDef>> PoolOptions, Dictionary<string, string> LogicOptions);
}
