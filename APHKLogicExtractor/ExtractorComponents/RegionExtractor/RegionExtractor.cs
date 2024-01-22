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
using System.Reflection;

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
        private static FieldInfo pathsField = typeof(DNFLogicDef)
            .GetField("paths", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static Type statePathType = pathsField.FieldType.GetElementType()!;
        private static MethodInfo toTermTokenSequence = statePathType
            .GetMethod("ToTermTokenSequence")!;

        private static Type logicDefBuilder = typeof(LogicManager).Assembly.GetType("RandomizerCore.Logic.DNFLogicDefBuilder", true)!;
        private static MethodInfo createDnfLogicDef = logicDefBuilder.GetMethod("CreateDNFLogicDef")!;

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
                builder.AddOrUpdateLogicObject(def, false);
            }
            foreach (RawWaypointDef waypoint in waypointLogic)
            {
                List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, waypoint.name);
                LogicObjectDefinition def = new(waypoint.name, clauses);
                builder.AddOrUpdateLogicObject(def, waypoint.stateless);
            }
            foreach (RawLogicDef location in locationLogic)
            {
                List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, location.name);
                LogicObjectDefinition def = new(location.name, clauses);
                builder.AddOrUpdateLogicObject(def, true);
            }

            logger.LogInformation("Simplifying region graph");
            foreach (Region region in builder.Regions.Values)
            {
                if (region.Locations.Count == 0 && region.Exits.Count == 0)
                {
                    logger.LogWarning($"Region {region.Name} has no exits or locations, rendering it useless");
                }
            }
            HashSet<Region> regionsExcludedFromMerge = [];
            while (true)
            {
                IEnumerable<Region> candidateRegions = builder.Regions.Values
                    .Where(x => !regionsExcludedFromMerge.Contains(x))
                    .Where(x => x.Locations.Any() && !x.Exits.Any() && builder.GetParents(x.Name).Count == 1);
                if (!candidateRegions.Any())
                {
                    break;
                }
                foreach (Region r in candidateRegions)
                {
                    if (CanAbsorb(builder, r, out Region parent))
                    {
                        parent.Locations.UnionWith(r.Locations);
                        builder.RemoveRegion(r.Name);
                    }
                    else
                    {
                        regionsExcludedFromMerge.Add(r);
                    }
                }
            }
            builder.Validate();
            List<Region> regions = builder.Build();

            logger.LogInformation("Beginning final output");
            GraphWorldDefinition world = new(regions);
            using (StreamWriter writer = outputManager.CreateOuputFileText("regions.json"))
            {
                using (JsonTextWriter jtw = new(writer))
                {
                    JsonSerializer ser = new JsonSerializer()
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
            logger.LogInformation("Successfully exported {} regions", regions.Count);
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

        private bool CanAbsorb(RegionGraphBuilder builder, Region r, out Region pparent)
        {
            Region parent = pparent = builder.GetParents(r.Name).First();
            IEnumerable<Region.Connection> conns = parent.Exits.Where(x => x.Target == r);
            IEnumerable<Region> grandparents = builder.GetParents(parent.Name);
            if (!grandparents.Any())
            {
                return conns.All(x => x.ItemRequirements.Count == 0 && x.LocationRequirements.Count == 0 && x.StateModifiers.Count == 0);
            }
            else
            {
                static bool ConnectionIsSuperset(Region.Connection c1, Region.Connection c2)
                {
                    if (!c1.ItemRequirements.IsSupersetOf(c2.ItemRequirements)
                        || !c1.LocationRequirements.IsSupersetOf(c2.LocationRequirements))
                    {
                        return false;
                    }

                    // this should compare state modifiers as well, but since we limit to cases where c2 has no state modifiers
                    // we can skip that. Long run we may want to do something like StatefulClause.HasSublistWithAdditionalModifiersOfKind
                    return true;
                }
                return conns.All(x => x.StateModifiers.Count == 0)
                    && grandparents.SelectMany(x => x.Exits).All(gpConn => conns.All(conn => ConnectionIsSuperset(gpConn, conn)));
            }
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
            Array statePaths = (Array)pathsField.GetValue(dd)!;
            List<StatefulClause> clausesForDef = [];
            foreach (object statePath in statePaths)
            {
                IEnumerable<IEnumerable<TermToken>> clausesForPath
                    = (IEnumerable<IEnumerable<TermToken>>)toTermTokenSequence.Invoke(statePath, [])!;
                clausesForDef.AddRange(clausesForPath.Select(x => new StatefulClause(lm, x.ToList())).ToList());
            }
            return clausesForDef;
        }
    }
}
