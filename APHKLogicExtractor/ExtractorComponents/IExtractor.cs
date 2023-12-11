using APHKLogicExtractor.Loaders;
using Microsoft.Extensions.Logging;

namespace APHKLogicExtractor.ExtractorComponents
{
    internal interface IExtractor
    {
        public Task ExtractAsync(ILoggerFactory loggerFactory, CommandLineOptions options, DataLoader dataLoader, LogicLoader logicLoader);
    }
}
