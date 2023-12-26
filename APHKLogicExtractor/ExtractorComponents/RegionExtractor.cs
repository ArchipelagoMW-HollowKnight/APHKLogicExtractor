using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loaders;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;
using System.Text.RegularExpressions;

namespace APHKLogicExtractor.ExtractorComponents
{
    internal class RegionExtractor(
        ILogger<RegionExtractor> logger, 
        IOptions<CommandLineOptions> optionsService,
        DataLoader dataLoader,
        LogicLoader logicLoader
    ) : BackgroundService
    {
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
            Dictionary<string, RawWaypointDef> statefulWaypoints= new();
            Dictionary<string, RawWaypointDef> statelessWaypoints = new();
            Dictionary<string, RawWaypointDef> warpWaypoints = new();
            
            foreach (RawWaypointDef waypoint in waypointLogic)
            {
                if (warpWaypointMatchers.Any(x => x.IsMatch(waypoint.name)))
                {
                    warpWaypoints[waypoint.name] = waypoint;
                    // we ignore warp waypoints from processing because they will act as transitions
                    options.IgnoredWaypoints.Add(waypoint.name);
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
            Dictionary<string, string> solvedWaypoints = new();
            foreach (RawWaypointDef wp in statefulWaypoints.Values)
            {
                // if it's a warp waypoint, it's actually acting as a transition so those are also skipped from solving
                if (!ShouldSolveWaypoint(wp.name))
                {
                    continue;
                }

                List<LogicToken> solvedLogicTokens = SolveWaypoint(wp, preprocessorLm, statefulWaypoints);
                string solvedLogic = Infix.ToInfix(solvedLogicTokens);
                logger.LogInformation("Simplified waypoint def {} to `{}`", wp.name, solvedLogic); ;
                solvedWaypoints.Add(wp.name, solvedLogic);
            }
            macroLogic = macroLogic.Concat(solvedWaypoints).ToDictionary();

            logger.LogInformation("Partitioning logic objects by scene");
            Dictionary<string, IEnumerable<RawLogicDef>> locationsByScene = locationLogic
                .GroupBy(l => locationData[l.name].SceneName)
                .ToDictionary(g => g.Key, g => (IEnumerable<RawLogicDef>)g);
            Dictionary<string, IEnumerable<RawLogicDef>> transitionsByScene = transitionLogic
                .GroupBy(t => transitionData[t.name].SceneName)
                .ToDictionary(g => g.Key, g => (IEnumerable<RawLogicDef>)g);

            logger.LogInformation("Creating regions for scenes");
            IEnumerable<string> scenes = options.Scenes != null ? options.Scenes : roomData.Keys;
            foreach (string scene in scenes)
            {
                logger.LogInformation("Processing scene {}", scene);
            }
        }

        private List<LogicToken> SolveWaypoint(RawWaypointDef waypoint, LogicManager lm, Dictionary<string, RawWaypointDef> statefulWaypoints)
        {
            LogicDef def = lm.GetLogicDefStrict(waypoint.name);
            HashSet<string> visited = [waypoint.name];
            List<LogicToken> tokens = def.ToTokenSequence().ToList();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i] is SimpleToken st)
                {
                    if (!statefulWaypoints.ContainsKey(st.Name) || !ShouldSolveWaypoint(st.Name))
                    {
                        // not a waypoint, at least not one we care about solving for.
                        continue;
                    }

                    LogicDef reference = lm.GetLogicDefStrict(st.Name);
                    List<LogicToken> referenceTokens = reference.ToTokenSequence().ToList();

                    if (visited.Contains(st.Name))
                    {
                        logger.LogError("Waypoint {} is cyclic (first cycle at {})", waypoint.name, st.Name);
                        // for now, bail out, return the original unmodified token sequence
                        return def.ToTokenSequence().ToList();
                    }

                    logger.LogInformation("In waypoint {}, substituting {} for `{}`", waypoint.name, st.Name, Infix.ToInfix(referenceTokens));
                    visited.Add(st.Name);
                    tokens.RemoveAt(i);
                    tokens.InsertRange(i, referenceTokens);
                    i--;
                }
            }
            if (tokens.Count == 0)
            {
                tokens.Add(ConstToken.False);
            }
            return tokens;
        }

        private bool ShouldSolveWaypoint(string waypoint)
        {
            return !options.IgnoredWaypoints.Contains(waypoint);
        }
    }
}
