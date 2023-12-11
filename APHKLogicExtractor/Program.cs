using APHKLogicExtractor;
using APHKLogicExtractor.ExtractorComponents;
using APHKLogicExtractor.Loaders;
using CommandLiners;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

IConfiguration config = new ConfigurationBuilder()
    .AddCommandLineOptions(args.ToPosix())
    .Build();

IServiceCollection services = new ServiceCollection();
services.AddOptions<CommandLineOptions>()
    .Bind(config)
    .ValidateDataAnnotations();

IServiceProvider provider = services.BuildServiceProvider();
IOptions<CommandLineOptions> optionsService = provider.GetRequiredService<IOptions<CommandLineOptions>>();

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
ILogger logger = loggerFactory.CreateLogger<Program>();

try
{
    CommandLineOptions options = optionsService.Value;
    DataLoader dataLoader = new(options.RefName);
    LogicLoader logicLoader = new(options.RefName);

    logger.LogInformation("Beginning extraction");
    IExtractor[] extractors = new[]
    {
        new RegionExtractor()
    };
    await Task.WhenAll(extractors.Select(x => x.ExtractAsync(loggerFactory, options, dataLoader, logicLoader)));
    logger.LogInformation("Completed extraction, output is in {}", Path.GetFullPath(options.OutputPath));
}
catch (OptionsValidationException ex)
{
    foreach (string failure in ex.Failures)
    {
        logger.LogError(failure);
    }
}

    