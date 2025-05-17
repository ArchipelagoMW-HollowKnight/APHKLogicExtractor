namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal record ItemEffectData(Dictionary<string, IItemEffect> ProgressionEffectLookup, 
        List<string> NonProgressionItems,
        Dictionary<string, IReadOnlySet<string>> AffectedTermsByItem,
        Dictionary<string, IReadOnlySet<string>> AffectingItemsByTerm);
}
