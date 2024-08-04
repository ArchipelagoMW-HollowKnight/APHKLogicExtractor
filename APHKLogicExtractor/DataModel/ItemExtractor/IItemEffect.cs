namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal interface IItemEffect
    {
        string Type { get; }

        IItemEffect? Simplify() => this;
    }
}
