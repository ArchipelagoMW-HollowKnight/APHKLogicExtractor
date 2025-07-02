using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loader;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RandomizerCore.Json;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;

namespace APHKLogicExtractor.ExtractorComponents;

internal class StringWorldCompositor(
    ILogger<StringWorldCompositor> logger
)
{
    public async Task<StringWorldDefinition> FromContext(MaybeFile<JToken> input)
    {
        logger.LogInformation("Loading world from saved RandoContext at {}", input);
        JToken ctxj = await input.GetContent<JToken>();
        ctxj["LM"]!["VariableResolver"]!["$type"] = "APHKLogicExtractor.RC.DummyVariableResolver, APHKLogicExtractor";
        LogicManager lm = JsonUtil.DeserializeFromToken<LogicManager>(ctxj["LM"]!)
            ?? throw new NullReferenceException("Got null value deserializing RandoContext");

        Dictionary<string, LogicWaypoint> waypointLookup = lm.Waypoints.ToDictionary(x => x.Name);
        List<LogicObjectDefinition> objects = [];
        foreach (LogicDef logic in lm.LogicLookup.Values)
        {
            LogicHandling handling;
            bool isEvent = false;
            if (lm.TransitionLookup.ContainsKey(logic.Name))
            {
                handling = LogicHandling.Transition;
            }
            else if (waypointLookup.TryGetValue(logic.Name, out LogicWaypoint? wp))
            {
                bool stateless = wp.term.Type != TermType.State;
                handling = stateless ? LogicHandling.Location : LogicHandling.Default;
                isEvent = stateless;
            }
            else
            {
                handling = LogicHandling.Location;
            }
            List<StatefulClause> clauses = RcUtils.GetDnfClauses(lm, logic.Name);
            objects.Add(new LogicObjectDefinition(logic.Name, clauses, handling, isEvent));
        }

        return new StringWorldDefinition(objects, lm);
    }

    public async Task<StringWorldDefinition> FromLogicFiles(MaybeFile<JToken> input)
    {
        logger.LogInformation("Constructing Rando4 logic from files");
        JsonLogicConfiguration configuration = await input.GetContent<JsonLogicConfiguration>();

        logger.LogInformation("Preparing logic manager");
        LogicManagerContext ctx = await RcUtils.ConstructLogicManager(configuration);
        
        List<LogicObjectDefinition> objects = [];
        // add waypoints to the region list first since they usually have better names after merging
        foreach (RawWaypointDef waypoint in ctx.WaypointLogic)
        {
            List<StatefulClause> clauses = RcUtils.GetDnfClauses(ctx.LogicManager, waypoint.name);
            LogicHandling handling = waypoint.stateless ? LogicHandling.Location : LogicHandling.Default;
            objects.Add(new LogicObjectDefinition(waypoint.name, clauses, handling, waypoint.stateless));
        }
        foreach (RawLogicDef transition in ctx.TransitionLogic)
        {
            List<StatefulClause> clauses = RcUtils.GetDnfClauses(ctx.LogicManager, transition.name);
            objects.Add(new LogicObjectDefinition(transition.name, clauses, LogicHandling.Transition));
        }
        foreach (RawLogicDef location in ctx.LocationLogic)
        {
            List<StatefulClause> clauses = RcUtils.GetDnfClauses(ctx.LogicManager, location.name);
            objects.Add(new LogicObjectDefinition(location.name, clauses, LogicHandling.Location));
        }

        return new StringWorldDefinition(objects, ctx.LogicManager);
    }
}
