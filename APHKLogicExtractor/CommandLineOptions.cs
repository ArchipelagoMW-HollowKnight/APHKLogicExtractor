using System.ComponentModel.DataAnnotations;

namespace APHKLogicExtractor
{
    public enum JobType
    {
        ExtractRegions,
        ExtractItems,
        ExtractData
    }

    internal record CommandLineOptions
    {
        /// <summary>
        /// Path to the input configuration file.
        /// </summary>
        [Required]
        public string? Input { get; set; }

        /// <summary>
        /// Path to the directory where the generated content will be placed.
        /// </summary>
        public string Output { get; set; } = "./output";

        /// <summary>
        /// Set of tasks that needs to run
        /// </summary>
        public HashSet<JobType> Jobs { get; set; } = [];

        /// <summary>
        /// When true, the resource cache will be ignored.
        /// </summary>
        public bool IgnoreCache { get; set; } = false;
    }
}
