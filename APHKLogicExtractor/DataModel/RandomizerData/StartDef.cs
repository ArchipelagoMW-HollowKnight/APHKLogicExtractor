namespace APHKLogicExtractor.DataModel.RandomizerData
{
    internal record StartDef
    {
        public required string Name { get; set; }
        public required string SceneName { get; set; }
        public required double X { get; set; }
        public required double Y { get; set; }
        public required string Zone { get; set; }
        public required string Transition { get; set; }
        public required string Logic { get; set; }
    }
}
