namespace APHKLogicExtractor
{
    internal record CommandLineOptions
    {
        public string RefName { get; set; } = "master";

        public List<string>? Scenes { get; set; }

        public string OutputPath { get; set; } = "./output";
    }
}
