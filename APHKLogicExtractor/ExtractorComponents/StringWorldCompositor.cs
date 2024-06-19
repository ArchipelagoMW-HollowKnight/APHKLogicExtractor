using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loader;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RandomizerCore.Json;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.ExtractorComponents;

internal class StringWorldCompositor(
    ILogger<StringWorldCompositor> logger,
    ResourceLoader resourceLoader
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

        return new StringWorldDefinition(objects);
    }

    public async Task<StringWorldDefinition> FromLogicFiles(MaybeFile<JToken> input)
    {
        logger.LogInformation("Constructing Rando4 logic from files");
        var configuration = await input.GetContent<JsonLogicConfiguration>();
        var rawTerms = (await configuration.logic?.terms?.GetContent()) ?? [];
        var terms = RC.Utils.assembleTerms(rawTerms);
        var stateData = await configuration?.logic?.state?.GetContent();
        var transitionLogic = await configuration?.logic?.transitions?.GetContent();
        var locationLogic = await configuration?.logic?.locations?.GetContent();
        var macroLogic = await configuration?.logic?.macros?.GetContent();
        var waypointLogic = await configuration?.logic?.waypoints?.GetContent();

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
        // add waypoints to the region list first since they usually have better names after merging
        foreach (RawWaypointDef waypoint in waypointLogic)
        {
            List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, waypoint.name);
            LogicHandling handling = waypoint.stateless ? LogicHandling.Location : LogicHandling.Default;
            objects.Add(new LogicObjectDefinition(waypoint.name, clauses, handling));
        }
        foreach (RawLogicDef transition in transitionLogic)
        {
            List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, transition.name);
            objects.Add(new LogicObjectDefinition(transition.name, clauses, LogicHandling.Transition));
        }
        foreach (RawLogicDef location in locationLogic)
        {
            List<StatefulClause> clauses = GetDnfClauses(preprocessorLm, location.name);
            objects.Add(new LogicObjectDefinition(location.name, clauses, LogicHandling.Location));
        }

        return new StringWorldDefinition(objects);
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

    private static List<StatefulClause> GetDnfClauses(LogicManager lm, DNFLogicDef dd)
    {
        // remove FALSE clauses, and remove TRUE from all clauses
        IEnumerable<IEnumerable<TermToken>> clauses = dd.ToTermTokenSequences()
            .Where(x => !x.Contains(ConstToken.False));
        if (!clauses.Any())
        {
            return [new StatefulClause(null, new HashSet<TermToken>(1) { ConstToken.False }, [])];
        }
        return clauses.Select(x => new StatefulClause(lm, x.Where(x => x != ConstToken.True))).ToList();
    }
}
