using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loaders;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{

    internal class RegionExtractor(
        ILogger<RegionExtractor> logger,
        IOptions<CommandLineOptions> optionsService,
        DataLoader dataLoader,
        LogicLoader logicLoader
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
            List<Regex> warpWaypointMatchers = options.WarpWaypoints.Select(w => new Regex($"^{w}$")).ToList();

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

            logger.LogInformation("Partitioning waypoints by statefulness");
            Dictionary<string, RawWaypointDef> statefulWaypoints = new();
            Dictionary<string, RawWaypointDef> statelessWaypoints = new();
            Dictionary<string, RawWaypointDef> warpWaypoints = new();

            foreach (RawWaypointDef waypoint in waypointLogic)
            {
                if (warpWaypointMatchers.Any(x => x.IsMatch(waypoint.name)))
                {
                    warpWaypoints[waypoint.name] = waypoint;
                    if (waypoint.stateless)
                    {
                        logger.LogWarning("Manually labeled warp waypoint is not stateful - {}", waypoint.name);
                    }
                    continue;
                }

                Dictionary<string, RawWaypointDef> dict = waypoint.stateless ? statelessWaypoints : statefulWaypoints;
                dict[waypoint.name] = waypoint;
            }

            logger.LogInformation("Solving stateful waypoint logic to macros");
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

            LogicManager preprocessorLm = new(preprocessorLmb);

            logger.LogInformation("Partitioning logic objects by scene");

            logger.LogInformation("Creating regions for scenes");
            IEnumerable<string> scenes = options.Scenes != null ? options.Scenes : roomData.Keys;
            foreach (string scene in scenes)
            {
                logger.LogInformation("Processing scene {}", scene);
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
