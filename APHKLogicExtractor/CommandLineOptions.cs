using System.ComponentModel.DataAnnotations;

namespace APHKLogicExtractor
{
    public enum JobType
    {
        ExtractRegions,
        ExtractItems
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
    }
}
