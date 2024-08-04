namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal interface IItemEffect
    {
        string Type { get; }

        IReadOnlySet<string> GetAffectedTerms();

        IItemEffect? Simplify() => this;
    }
}
