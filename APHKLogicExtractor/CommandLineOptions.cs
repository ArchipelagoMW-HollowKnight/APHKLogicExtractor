namespace APHKLogicExtractor
{
    enum WaypointCycleHandling
    {
        Crash, TreatSelfReferencesAsFalse
    }

    internal record CommandLineOptions
    {
        public string RefName { get; set; } = "master";

        public List<string> WarpWaypoints { get; set; } = new();

        public HashSet<string> IgnoredWaypoints { get; set; } = new();

        public List<string>? Scenes { get; set; }

        public string OutputPath { get; set; } = "./output";

    }
}
