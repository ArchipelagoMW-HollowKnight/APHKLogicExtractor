using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loaders;
using Microsoft.Extensions.Logging;
using RandomizerCore.Logic;
using System.Text.RegularExpressions;

namespace APHKLogicExtractor.ExtractorComponents
{
    internal class RegionExtractor : IExtractor
    {
        public async Task ExtractAsync(ILoggerFactory loggerFactory, CommandLineOptions options, DataLoader dataLoader, LogicLoader logicLoader)
        {
            ILogger logger = loggerFactory.CreateLogger<RegionExtractor>();
            logger.LogInformation("Validating options");
            bool overlap = options.ManualStatefulWaypoints.Intersect(options.ManualStatelessWaypoints).Any();
            if (overlap)
            {
                logger.LogError("Found overlapping manual statefulness labels, exiting early.");
                return;
            }
            List<Regex> warpWaypointMatchers = options.WarpWaypoints.Select(w => new Regex($"^{w}$")).ToList();

            logger.LogInformation("Beginning region extraction");

            logger.LogInformation("Fetching data and logic");
            Dictionary<string, RoomDef> roomData = await dataLoader.LoadRooms();
            Dictionary<string, TransitionDef> transitionData = await dataLoader.LoadTransitions();
            Dictionary<string, LocationDef> locationData = await dataLoader.LoadLocations();

            List<RawLogicDef> transitionLogic = await logicLoader.LoadTransitions();
            List<RawLogicDef> locationLogic = await logicLoader.LoadLocations();
            Dictionary<string, string> macroLogic = await logicLoader.LoadMacros();
            List<RawWaypointDef> waypointLogic = await logicLoader.LoadWaypoints();

            logger.LogInformation("Partitioning waypoints by statefulness");
            Dictionary<string, string> statefulWaypointLogic = new();
            Dictionary<string, string> statelessWaypointLogic = new();
            Dictionary<string, string> warpWaypointLogic = new();

            void PartitionWaypoint(RawWaypointDef waypoint, bool defaultStatelessness)
            {
                if (warpWaypointMatchers.Any(x => x.IsMatch(waypoint.name)))
                {
                    warpWaypointLogic[waypoint.name] = waypoint.logic;
                    return;
                }

                bool stateless = defaultStatelessness;
                if (options.ManualStatelessWaypoints.Contains(waypoint.name))
                {
                    if (options.ManualStatefulWaypoints.Contains(waypoint.name))
                    {
                        logger.LogWarning("Waypoint {} is manually labelled as both stateful and stateless", waypoint.name);
                    }
                    stateless = true;
                }
                else if (options.ManualStatefulWaypoints.Contains(waypoint.name))
                {
                    stateless = false;
                }

                Dictionary<string, string> dict = stateless ? statelessWaypointLogic : statefulWaypointLogic;
                dict[waypoint.name] = waypoint.logic;
            }

            if (options.WaypointStatefulnessRefName != null)
            {
                LogicLoader altLoader = new(options.WaypointStatefulnessRefName);
                var altWaypointLogic = await altLoader.LoadWaypoints();
                Dictionary<string, bool> isWaypointStatelessByName = new();
                foreach (RawWaypointDef waypoint in altWaypointLogic)
                {
                    isWaypointStatelessByName[waypoint.name] = waypoint.stateless;
                }

                foreach (RawWaypointDef waypoint in waypointLogic)
                {
                    if (!isWaypointStatelessByName.TryGetValue(waypoint.name, out bool stateless))
                    {
                        logger.LogWarning("Didn't find the waypoint {} in statefulness ref {}", waypoint.name, options.WaypointStatefulnessRefName);
                        stateless = waypoint.stateless;
                    }
                    PartitionWaypoint(waypoint, stateless);
                }
            }
            else
            {
                foreach (RawWaypointDef waypoint in waypointLogic)
                {
                    PartitionWaypoint(waypoint, waypoint.stateless);
                }
            }

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
    }
}
