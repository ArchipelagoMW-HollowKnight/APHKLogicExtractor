namespace APHKLogicExtractor
{
    internal record CommandLineOptions
    {
        public string OutputPath { get; set; } = "./output";

        // Region extractor options

        public string RefName { get; set; } = "master";

        public string? StartStateTerm { get; set; }

        public string? WorldDefinitionPath { get; set; }

        public string? RandoContextPath { get; set; }

        public string? ClassifierModelPath { get; set; }
    }
}
