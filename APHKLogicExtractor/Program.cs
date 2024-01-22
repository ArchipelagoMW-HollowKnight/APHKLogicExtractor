using APHKLogicExtractor;
using APHKLogicExtractor.ExtractorComponents;
using APHKLogicExtractor.ExtractorComponents.RegionExtractor;
using APHKLogicExtractor.Loaders;
using CommandLiners;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddCommandLineOptions(args.ToPosix());

builder.Services.AddOptions<CommandLineOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(provider =>
{
    IOptions<CommandLineOptions> options = provider.GetRequiredService<IOptions<CommandLineOptions>>();
    return new DataLoader(options.Value.RefName);
});
builder.Services.AddSingleton(provider =>
{
    IOptions<CommandLineOptions> options = provider.GetRequiredService<IOptions<CommandLineOptions>>();
    return new LogicLoader(options.Value.RefName);
});
builder.Services.AddSingleton<OutputManager>();
builder.Services.AddSingleton<VariableParser>();
builder.Services.AddSingleton<StateModifierClassifier>();
builder.Services.AddHostedService<RegionExtractor>();

IHost host = builder.Build();
await host.StartAsync();
IEnumerable<Task> tasks = host.Services.GetServices<IHostedService>()
    .OfType<BackgroundService>()
    .Where(x => x.ExecuteTask != null)
    .Select(x => x.ExecuteTask!);
await Task.WhenAll(tasks);

    