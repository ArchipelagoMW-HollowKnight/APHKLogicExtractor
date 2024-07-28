namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal record ItemData(Dictionary<string, IItemEffect> ProgressionEffectLookup, 
        List<string> NonProgressionItems);
}
