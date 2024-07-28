namespace APHKLogicExtractor.DataModel.RandomizerData
{
    internal record RoomDef
    {
        public required string SceneName { get; set; }
        public required string MapArea { get; set; }
        public required string TitledArea { get; set; }
    }
}
