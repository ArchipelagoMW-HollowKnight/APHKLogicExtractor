using APHKLogicExtractor.DataModel.RandomizerData;

namespace APHKLogicExtractor.DataModel.DataExtractor
{
    internal record ApRandomizedPool(List<string> Items, List<string> Locations);
    internal record ApPoolDef(ApRandomizedPool Randomized, List<VanillaDef> Vanilla)
    {
        public static ApPoolDef Empty()
        {
            return new ApPoolDef(new ApRandomizedPool([], []), []);
        }
    }
    internal record PoolData(Dictionary<string, ApPoolDef> PoolOptions, Dictionary<string, string> LogicOptions);
}
