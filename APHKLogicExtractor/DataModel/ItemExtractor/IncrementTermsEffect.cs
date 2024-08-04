namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal record IncrementTermsEffect(IReadOnlyDictionary<string, int> Effects) : IItemEffect
    {
        public string Type => "incrementTerms";
    }
}
