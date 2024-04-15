namespace APHKLogicExtractor
{
    public enum JobType
    {
        ExtractRegions,
        ExtractItems
    }

    internal record CommandLineOptions
    {
        public string OutputPath { get; set; } = "./output";

        public HashSet<JobType> Jobs { get; set; } = new();

        // Region extractor options

        public string RefName { get; set; } = "master";

        public string? StartStateTerm { get; set; }

        public string? WorldDefinitionPath { get; set; }

        public string? RandoContextPath { get; set; }

        public string? ClassifierModelPath { get; set; }

        public string? EmptyRegionsToKeepPath { get; set; }
    }
}
