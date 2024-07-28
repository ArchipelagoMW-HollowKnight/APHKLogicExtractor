namespace APHKLogicExtractor.DataModel.RandomizerData
{
    internal record LocationDef
    {
        public required string Name { get; set; }
        public required string SceneName { get; set; }
        public required bool FlexibleCount { get; set; }
        public required bool AdditionalProgressionPenalty { get; set; }
    }
}
