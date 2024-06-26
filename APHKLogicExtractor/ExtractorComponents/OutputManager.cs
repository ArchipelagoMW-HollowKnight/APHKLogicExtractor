﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace APHKLogicExtractor.ExtractorComponents
{
    internal class OutputManager(
        ILogger<OutputManager> logger,
        IOptions<CommandLineOptions> optionsService)
    {
        private CommandLineOptions options = optionsService.Value;

        private void EnsureOutputPath()
        {
            Directory.CreateDirectory(options.Output);
        }

        public Stream CreateOutputFile(string name)
        {
            EnsureOutputPath();
            logger.LogDebug("Creating output file: {}", name);
            return File.Create(Path.Combine(options.Output, name));
        }

        public StreamWriter CreateOuputFileText(string name)
        {
            EnsureOutputPath();
            logger.LogDebug("Creating output file: {}", name);
            return File.CreateText(Path.Combine(options.Output, name));
        }
    }
}
