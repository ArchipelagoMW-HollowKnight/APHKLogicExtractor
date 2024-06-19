using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loader;
using DotNetGraph.Compilation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RandomizerCore.Json;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal class RegionExtractor(
        ApplicationInput input,
        ILogger<RegionExtractor> logger,
        IOptions<CommandLineOptions> optionsService,
        StringWorldCompositor stringWorldCompositor,
        StateModifierClassifier stateClassifier,
        Pythonizer pythonizer,
        OutputManager outputManager
    ) : BackgroundService
    {
        private CommandLineOptions options = optionsService.Value;

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Validating options");

            if (options.Jobs.Any() && !options.Jobs.Contains(JobType.ExtractRegions))
            {
                logger.LogInformation("Job not requested, skipping");
                return;
            }

            logger.LogInformation("Beginning region extraction");

            StringWorldDefinition worldDefinition = await (input.type switch
            {
                InputType.WorldDefinition => input.configuration.GetContent<StringWorldDefinition>(),
                InputType.RandoContext => stringWorldCompositor.FromContext(input.configuration),
                InputType.JsonLogic => stringWorldCompositor.FromLogicFiles(input.configuration),
                _ => throw new NotImplementedException(),
            });

            logger.LogInformation("Creating initial region graph");
            RegionGraphBuilder builder = new();
            foreach (LogicObjectDefinition obj in worldDefinition.LogicObjects)
            {
                builder.AddOrUpdateLogicObject(obj);
            }
            if (input.startStateTerm != null)
            {
                logger.LogInformation($"Rebasing start state from {input.startStateTerm} onto Menu");
                builder.LabelRegionAsMenu(input.startStateTerm);
            }

            logger.LogInformation("Beginning final output");
            HashSet<string>? regionsToKeep = null;
            if (input.emptyRegionsToKeep != null)
            {
                regionsToKeep = await input.emptyRegionsToKeep.GetContent();
            }
            GraphWorldDefinition world = builder.Build(stateClassifier, regionsToKeep);
            using (StreamWriter writer = outputManager.CreateOuputFileText("regions.json"))
            {
                using (JsonTextWriter jtw = new(writer))
                {
                    JsonSerializer ser = new()
                    {
                        Formatting = Formatting.Indented,
                    };
                    ser.Serialize(jtw, world);
                }
            }
            using (StreamWriter writer = outputManager.CreateOuputFileText("region_data.py"))
            {
                pythonizer.Write(world, writer);
            }
            using (StreamWriter writer = outputManager.CreateOuputFileText("regionGraph.dot"))
            {
                CompilationContext ctx = new(writer, new CompilationOptions());
                await builder.BuildDotGraph().CompileAsync(ctx);
            }
            logger.LogInformation("Successfully exported {} regions ({} empty) and {} locations",
                world.Regions.Count(),
                world.Regions.Where(r => r.Locations.Count == 0 && r.Transitions.Count == 0).Count(),
                world.Locations.Count());
        }
    }
}
