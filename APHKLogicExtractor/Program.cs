using APHKLogicExtractor;
using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.ExtractorComponents;
using APHKLogicExtractor.ExtractorComponents.DataExtractor;
using APHKLogicExtractor.ExtractorComponents.ItemExtractor;
using APHKLogicExtractor.ExtractorComponents.RegionExtractor;
using APHKLogicExtractor.Loader;
using CommandLiners;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RandomizerCore.Json;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddCommandLineOptions(args.ToPosix());

builder.Services.AddOptions<CommandLineOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<ResourceLoader>();
builder.Services.AddSingleton(provider =>
{
    IOptions<CommandLineOptions> options = provider.GetRequiredService<IOptions<CommandLineOptions>>();
    ResourceLoader resourceLoader = provider.GetRequiredService<ResourceLoader>();

    return JsonUtils.GetSerializer(resourceLoader).DeserializeFromFile<ApplicationInput>(options.Value.Input!) ??
        throw new NullReferenceException("Failed to parse the input file");
});
builder.Services.AddSingleton<Pythonizer>();
builder.Services.AddSingleton<OutputManager>();
builder.Services.AddSingleton<VariableParser>();
builder.Services.AddSingleton<StateModifierClassifier>();
builder.Services.AddSingleton<StringWorldCompositor>();

builder.Services.AddHostedService<RegionExtractor>();
builder.Services.AddHostedService<ItemExtractor>();
builder.Services.AddHostedService<DataExtractor>();

IHost host = builder.Build();
await host.StartAsync();
IEnumerable<Task> tasks = host.Services.GetServices<IHostedService>()
    .OfType<BackgroundService>()
    .Where(x => x.ExecuteTask != null)
    .Select(x => x.ExecuteTask!);
await Task.WhenAll(tasks);

if (host.Services.GetService<IOptions<CommandLineOptions>>() is IOptions<CommandLineOptions> opt && opt.Value.Bundle)
{
    host.Services.GetService<OutputManager>()?.Bundle();
}

