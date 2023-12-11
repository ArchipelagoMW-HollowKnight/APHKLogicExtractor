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

        public string? WaypointStatefulnessRefName { get; set; }

        public HashSet<string> ManualStatefulWaypoints { get; set; } = new();

        public HashSet<string> ManualStatelessWaypoints { get; set; } = new();

        public WaypointCycleHandling WaypointCycleHandling { get; set; } = WaypointCycleHandling.Crash;

        public List<string>? Scenes { get; set; }

        public string OutputPath { get; set; } = "./output";

    }
}
