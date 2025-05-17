namespace APHKLogicExtractor.DataModel.RandomizerData
{
    internal record CostDef(string Term, int Amount);
    internal record VanillaDef(string Item, string Location)
    {
        public List<CostDef>? Costs { get; set; } = null;
    }

    internal record PoolDef
    {
        public required string Group { get; set; }
        public required string Name { get; set; }
        public required string Path { get; set; }
        public required List<string> IncludeItems { get; set; }
        public required List<string> IncludeLocations { get; set; }
        public required List<VanillaDef> Vanilla { get; set; }
    }
}
