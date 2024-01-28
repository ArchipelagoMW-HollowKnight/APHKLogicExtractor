using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loaders;
using APHKLogicExtractor.RC;
using DotNetGraph.Compilation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RandomizerCore.Json;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal class RegionExtractor(
        ILogger<RegionExtractor> logger,
        IOptions<CommandLineOptions> optionsService,
        DataLoader dataLoader,
        LogicLoader logicLoader,
        StateModifierClassifier stateClassifier,
        OutputManager outputManager
    ) : BackgroundService
    {
        private CommandLineOptions options = optionsService.Value;

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Validating options");

            logger.LogInformation("Beginning region extraction");

            StringWorldDefinition worldDefinition;
            if (options.WorldDefinitionPath != null)
            {
                logger.LogInformation("Loading world from definition file at {}", options.WorldDefinitionPath);
                worldDefinition = JsonUtil.DeserializeFromFile<StringWorldDefinition>(options.WorldDefinitionPath)
                    ?? throw new NullReferenceException("Got null value deserializing world definition");
            }
            else if (options.RandoContextPath != null)
            {
                logger.LogInformation("Loading world from saved RandoContext at {}", options.RandoContextPath);
                JToken ctxj = JsonUtil.DeserializeFromFile<JToken>(options.RandoContextPath) 
                    ?? throw new NullReferenceException("Got null value deserializing RandoContext");
                ctxj["LM"]!["VariableResolver"]!["$type"] = "APHKLogicExtractor.RC.DummyVariableResolver, APHKLogicExtractor";
                LogicManager lm = JsonUtil.DeserializeFromToken<LogicManager>(ctxj["LM"]!)
                    ?? throw new NullReferenceException("Got null value deserializing RandoContext");
                Dictionary<string, LogicWaypoint> waypointLookup = lm.Waypoints.ToDictionary(x => x.Name);

                List<LogicObjectDefinition> objects = [];
                foreach (LogicDef logic in lm.LogicLookup.Values)
                {
                    LogicHandling handling;
                    if (lm.TransitionLookup.ContainsKey(logic.Name))
                    {
                        handling = LogicHandling.Transition;
                    }
                    else if (waypointLookup.TryGetValue(logic.Name, out LogicWaypoint? wp))
                    {
                        bool stateless = wp.term.Type != TermType.State;
                        handling = stateless ? LogicHandling.Location : LogicHandling.Default;
                    }
                    else
                    {
                        handling = LogicHandling.Location;
                    }
                    List<StatefulClause> clauses = GetDnfClauses(lm, logic.Name);
                    objects.Add(new LogicObjectDefinition(logic.Name, clauses, handling));
                }
                worldDefinition = new StringWorldDefinition(objects);
            }
            else
            {
                logger.LogInformation("Constructing Rando4 logic from remote ref {}", options.RefName);

                logger.LogInformation("Fetching data and logic");
                Dictionary<string, RoomDef> roomData = await dataLoader.LoadRooms();
                Dictionary<string, TransitionDef> transitionData = await dataLoader.LoadTransitions();
                Dictionary<string, LocationDef> locationData = await dataLoader.LoadLocations();

                TermCollectionBuilder terms = await logicLoader.LoadTerms();
                RawStateData stateData = await logicLoader.LoadStateFields();
                List<RawLogicDef> transitionLogic = await logicLoader.LoadTransitions();
                List<RawLogicDef> locationLogic = await logicLoader.LoadLocations();
                Dictionary<string, string> macroLogic = await logicLoader.LoadMacros();
                List<RawWaypointDef> waypointLogic = await logicLoader.LoadWaypoints();

                logger.LogInformation("Preparing logic manager");
                LogicManagerBuilder preprocessorLmb = new() { VariableResolver = new DummyVariableResolver() };
                preprocessorLmb.LP.SetMacro(macroLogic);
                preprocessorLmb.StateManager.AppendRawStateData(stateData);
                foreach (Term term in terms)
                {
                    preprocessorLmb.GetOrAddTerm(term.Name, term.Type);
                }
                foreach (RawWaypointDef wp in waypointLogic)
                {
                    preprocessorLmb.AddWaypoint(wp);
                }
                foreach (RawLogicDef transition in transitionLogic)
                {
                    preprocessorLmb.AddTransition(transition);
                }
                foreach (RawLogicDef location in locationLogic)
                {
                    preprocessorLmb.AddLogicDef(location);
                }

                LogicManager preprocessorLm = new(preprocessorLmb);
                List<LogicObjectDefinition> objects = [];
                foreach (RawLogicDef transition in transitionLogic)
                {
                    List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, transition.name);
                    objects.Add(new LogicObjectDefinition(transition.name, clauses, LogicHandling.Transition));
                }
                foreach (RawWaypointDef waypoint in waypointLogic)
                {
                    List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, waypoint.name);
                    LogicHandling handling = waypoint.stateless ? LogicHandling.Location : LogicHandling.Default;
                    objects.Add(new LogicObjectDefinition(waypoint.name, clauses, handling));
                }
                foreach (RawLogicDef location in locationLogic)
                {
                    List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, location.name);
                    objects.Add(new LogicObjectDefinition(location.name, clauses, LogicHandling.Location));
                }
                worldDefinition = new StringWorldDefinition(objects);
            }

            logger.LogInformation("Creating initial region graph");
            RegionGraphBuilder builder = new();
            foreach (LogicObjectDefinition obj in worldDefinition.LogicObjects)
            {
                builder.AddOrUpdateLogicObject(obj);
            }

            logger.LogInformation("Beginning final output");
            foreach (Region region in builder.Regions.Values)
            {
                if (region.Locations.Count == 0 && region.Exits.Count == 0)
                {
                    logger.LogWarning($"Region {region.Name} has no exits or locations, rendering it useless");
                }
            }
            GraphWorldDefinition world = builder.Build(stateClassifier);
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
            using (StreamWriter writer = outputManager.CreateOuputFileText("regionGraph.dot"))
            {
                CompilationContext ctx = new(writer, new CompilationOptions());
                await builder.BuildDotGraph().CompileAsync(ctx);
            }
            logger.LogInformation("Successfully exported {} regions ({} empty) and {} locations", 
                world.Regions.Count(), 
                world.Regions.Where(r => r.Locations.Count == 0).Count(),
                world.Locations.Count());
        }

        private List<StatefulClause> GetDnfClauses(LogicManager lm, string name)
        {
            LogicDef def = lm.GetLogicDefStrict(name);
            if (def is not DNFLogicDef dd)
            {
                logger.LogWarning("Logic definition for {} was not available in DNF form, creating", def.Name);
                dd = lm.CreateDNFLogicDef(def.Name, def.ToLogicClause());
            }
            return GetDnfClauses(lm, dd);
        }

        private List<StatefulClause> GetDnfClauses(LogicManager lm, DNFLogicDef dd)
        {
            // remove FALSE clauses, and remove TRUE from all clauses
            IEnumerable<IEnumerable<TermToken>> clauses = dd.ToTermTokenSequences()
                .Where(x => !x.Contains(ConstToken.False));
            return clauses.Select(x => new StatefulClause(lm, x.Where(x => x != ConstToken.True))).ToList();
        }
    }
}
