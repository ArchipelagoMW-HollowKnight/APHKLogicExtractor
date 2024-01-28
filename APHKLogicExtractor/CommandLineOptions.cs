namespace APHKLogicExtractor
{
    internal record CommandLineOptions
    {
        public string RefName { get; set; } = "master";

        public string OutputPath { get; set; } = "./output";

        public string? WorldDefinitionPath { get; set; }

        public string? RandoContextPath { get; set; }
    }
}
