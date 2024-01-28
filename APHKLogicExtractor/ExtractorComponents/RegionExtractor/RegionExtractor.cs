using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loaders;
using APHKLogicExtractor.RC;
using DotNetGraph.Compilation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
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

            logger.LogInformation("Creating initial region graph");
            RegionGraphBuilder builder = new();
            foreach (RawLogicDef transition in transitionLogic)
            {
                List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, transition.name);
                LogicObjectDefinition def = new(transition.name, clauses);
                builder.AddOrUpdateLogicObject(def, false, true);
            }
            foreach (RawWaypointDef waypoint in waypointLogic)
            {
                List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, waypoint.name);
                LogicObjectDefinition def = new(waypoint.name, clauses);
                builder.AddOrUpdateLogicObject(def, waypoint.stateless, false);
            }
            foreach (RawLogicDef location in locationLogic)
            {
                List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, location.name);
                LogicObjectDefinition def = new(location.name, clauses);
                builder.AddOrUpdateLogicObject(def, true, false);
            }

            logger.LogInformation("Beginning final output");
            foreach (Region region in builder.Regions.Values)
            {
                if (region.Locations.Count == 0 && region.Exits.Count == 0)
                {
                    logger.LogWarning($"Region {region.Name} has no exits or locations, rendering it useless");
                }
            }
            builder.Validate();
            GraphWorldDefinition world = builder.Build();
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

        private List<StatefulClause> RemoveRedundantClauses(List<StatefulClause> clauses)
        {
            List<StatefulClause> result = new(clauses);
            for (int i = 0; i < result.Count - 1; i++)
            {
                StatefulClause ci = result[i];
                for (int j = i + 1; j < result.Count; j++)
                {
                    StatefulClause cj = result[j];
                    if (cj.IsSameOrBetterThan(ci, stateClassifier))
                    {
                        // the right clause is better than the left clause. drop the left clause.
                        // this will result in needing to select a new left clause so break out.
                        result.RemoveAt(i);
                        i--;
                        break;
                    }
                    else if (ci.IsSameOrBetterThan(cj, stateClassifier))
                    {
                        // the left clause is better than the right clause. drop the right clause.
                        // continuing the loop will select the correct right clause next.
                        result.RemoveAt(j);
                        j--;
                    }
                }
            }
            return result;
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
