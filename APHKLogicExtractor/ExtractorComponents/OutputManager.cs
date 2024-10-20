using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;

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

        public string GetOutputPath(string name)
        {
            return Path.GetFullPath(Path.Combine(options.Output, name));
        }

        public Stream CreateOutputFile(string name)
        {
            string fullPath = GetOutputPath(name);
            EnsureOutputPath(fullPath);
            logger.LogDebug("Creating output file: {}", name);
            return File.Create(fullPath);
        }

        public StreamWriter CreateOuputFileText(string name)
        {
            string fullPath = GetOutputPath(name);
            EnsureOutputPath(fullPath);
            logger.LogDebug("Creating output file: {}", name);
            return File.CreateText(fullPath);
        }

        public void Bundle()
        {
            string bundleName = Path.GetFileName(Path.TrimEndingDirectorySeparator(options.Output)) + ".zip";
            string fullPath = GetOutputPath(bundleName);
            EnsureOutputPath(fullPath);
            try
            {
                File.Delete(fullPath);
            }
            catch { }

            string temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            ZipFile.CreateFromDirectory(options.Output, temp);
            File.Move(temp, fullPath);
        }
    }
}
