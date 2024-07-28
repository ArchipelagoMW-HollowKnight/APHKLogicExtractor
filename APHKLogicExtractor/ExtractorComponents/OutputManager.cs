using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APHKLogicExtractor.ExtractorComponents
{
    internal class OutputManager(
        ILogger<OutputManager> logger,
        IOptions<CommandLineOptions> optionsService)
    {
        private CommandLineOptions options = optionsService.Value;

        private void EnsureOutputPath(string outputFullPath)
        {
            string dir = Path.GetDirectoryName(outputFullPath) ?? options.Output;
            Directory.CreateDirectory(dir);
        }

        public Stream CreateOutputFile(string name)
        {
            string fullPath = Path.Combine(options.Output, name);
            EnsureOutputPath(fullPath);
            logger.LogDebug("Creating output file: {}", name);
            return File.Create(fullPath);
        }

        public StreamWriter CreateOuputFileText(string name)
        {
            string fullPath = Path.Combine(options.Output, name);
            EnsureOutputPath(fullPath);
            logger.LogDebug("Creating output file: {}", name);
            return File.CreateText(fullPath);
        }
    }
}
