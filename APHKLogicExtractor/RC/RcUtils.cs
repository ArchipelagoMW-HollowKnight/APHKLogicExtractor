using APHKLogicExtractor.DataModel;
using Microsoft.Extensions.Logging;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringItems;
using RandomizerCore.StringLogic;
using RandomizerCore.StringParsing;

namespace APHKLogicExtractor.RC;

internal class RcUtils
{
    public static TermCollectionBuilder AssembleTerms(Dictionary<string, List<string>> terms)
    {
        TermCollectionBuilder termsBuilder = new();
        foreach ((string type, List<string> termsOfType) in terms)
        {
            TermType termType = (TermType)Enum.Parse(typeof(TermType), type);
            foreach (string term in termsOfType)
            {
                termsBuilder.GetOrAddTerm(term, termType);
            }
        }
        return termsBuilder;
    }

    public static async Task<LogicManagerContext> ConstructLogicManager(JsonLogicConfiguration configuration)
    {
        Dictionary<string, List<string>> rawTerms = [];
        if (configuration.Logic?.Terms != null)
        {
            rawTerms = await configuration.Logic.Terms.GetContent();
        }

        TermCollectionBuilder terms = AssembleTerms(rawTerms);
        RawStateData stateData = new();
        if (configuration?.Logic?.State != null)
        {
            stateData = await configuration.Logic.State.GetContent();
        }

        List<RawLogicDef> transitionLogic = [];
        if (configuration?.Logic?.Transitions != null)
        {
            transitionLogic = await configuration.Logic.Transitions.GetContent();
        }

        List<RawLogicDef> locationLogic = [];
        if (configuration?.Logic?.Locations != null)
        {
            locationLogic = await configuration.Logic.Locations.GetContent();
        }

        Dictionary<string, string> macroLogic = [];
        if (configuration?.Logic?.Macros != null)
        {
            macroLogic = await configuration.Logic.Macros.GetContent();
        }

        List<RawWaypointDef> waypointLogic = [];
        if (configuration?.Logic?.Waypoints != null)
        {
            waypointLogic = await configuration.Logic.Waypoints.GetContent();
        }

        List<StringItemTemplate> itemTemplates = [];
        if (configuration?.Logic?.Items != null)
        {
            itemTemplates = await configuration.Logic.Items.GetContent();
        }

        LogicManagerBuilder lmb = new() { VariableResolver = new DummyVariableResolver(stateData) };
        foreach (KeyValuePair<string, string> macro in macroLogic)
        {
            lmb.AddMacro(macro);
        }
        foreach (Term term in terms)
        {
            lmb.GetOrAddTerm(term.Name, term.Type);
        }
        foreach (RawWaypointDef wp in waypointLogic)
        {
            lmb.AddWaypoint(wp);
        }
        foreach (RawLogicDef transition in transitionLogic)
        {
            lmb.AddTransition(transition);
        }
        foreach (RawLogicDef location in locationLogic)
        {
            lmb.AddLogicDef(location);
        }
        foreach (StringItemTemplate template in itemTemplates)
        {
            lmb.AddItem(template);
        }
        LogicManager lm = new(lmb);

        return new LogicManagerContext(
            lm, terms, stateData, transitionLogic, locationLogic,
            macroLogic, waypointLogic, itemTemplates);
    }

    public static List<StatefulClause> GetDnfClauses(LogicManager lm, string name, ILogger? logger = null)
    {
        LogicDef def = lm.GetLogicDefStrict(name);
        if (def is not DNFLogicDef dd)
        {
            dd = lm.CreateDNFLogicDef(def.Name, def.ToLogicClause());
        }
        return GetDnfClauses(lm, dd, logger);
    }

    public static List<StatefulClause> GetDnfClauses(LogicManager lm, DNFLogicDef dd, ILogger? logger = null)
    {
        LogicExpressionBuilder builder = new();
        List<DNFLogicDef.ReadOnlyConjunction> conjunctions = [.. dd.GetAllConjunctions()];
        if (conjunctions.Count == 0)
        {
            return [new StatefulClause(null, new HashSet<Expression<LogicExpressionType>>(1) { builder.NameAtom("NONE") }, [])];
        }

        return [.. conjunctions.Select(c => new StatefulClause(lm, c))];
    }
}
