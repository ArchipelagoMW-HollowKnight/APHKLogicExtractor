namespace APHKLogicExtractor.DataModel.RandomizerData
{
    internal record ItemDef
    {
        public required string Name { get; set; }
        public required string Pool { get; set; }
        public required int PriceCap { get; set; }
        public required bool MajorItem { get; set; }
    }
}
