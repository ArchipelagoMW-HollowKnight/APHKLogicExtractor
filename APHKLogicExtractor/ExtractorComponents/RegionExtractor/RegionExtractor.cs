﻿using APHKLogicExtractor.DataModel;
using DotNetGraph.Compilation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

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

            StringWorldDefinition worldDefinition = await (input.Type switch
            {
                InputType.WorldDefinition => input.Configuration.GetContent<StringWorldDefinition>(),
                InputType.RandoContext => stringWorldCompositor.FromContext(input.Configuration),
                InputType.JsonLogic => stringWorldCompositor.FromLogicFiles(input.Configuration),
                _ => throw new NotImplementedException(),
            });

            logger.LogInformation("Creating initial region graph");
            RegionGraphBuilder builder = new();
            foreach (LogicObjectDefinition obj in worldDefinition.LogicObjects)
            {
                builder.AddOrUpdateLogicObject(obj, worldDefinition.BackingLm);
            }
            if (input.StartStateTerm != null)
            {
                logger.LogInformation($"Rebasing start state from {input.StartStateTerm} onto Menu");
                builder.LabelRegionAsMenu(input.StartStateTerm);
            }

            logger.LogInformation("Beginning final output");
            HashSet<string>? regionsToKeep = null;
            if (input.EmptyRegionsToKeep != null)
            {
                regionsToKeep = await input.EmptyRegionsToKeep.GetContent();
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
            using (StreamWriter writer = outputManager.CreateOuputFileText("constants/location_names.py"))
            {
                pythonizer.WriteEnum("LocationNames", 
                    world.Locations.Where(l => !l.IsEvent).Select(l => l.Name),
                    writer);
            }
            using (StreamWriter writer = outputManager.CreateOuputFileText("constants/event_names.py"))
            {
                pythonizer.WriteEnum("EventNames",
                    world.Locations.Where(l => l.IsEvent).Select(l => l.Name),
                    writer);
            }
            using (StreamWriter writer = outputManager.CreateOuputFileText("constants/transition_names.py"))
            {
                pythonizer.WriteEnum("TransitionNames",
                    world.Transitions.Select(t => t.Name),
                    writer);
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
