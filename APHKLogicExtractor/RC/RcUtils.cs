using APHKLogicExtractor.DataModel;
using RandomizerCore.Logic;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.RC;

internal class RcUtils
{
    public static TermCollectionBuilder AssembleTerms(Dictionary<string, List<string>> terms)
    {
        TermCollectionBuilder termsBuilder = new();
        foreach (var (type, termsOfType) in terms)
        {
            TermType termType = (TermType)Enum.Parse(typeof(TermType), type);
            foreach (string term in termsOfType)
            {
                termsBuilder.GetOrAddTerm(term, termType);
            }
        }
        return termsBuilder;
    }

    public static List<StatefulClause> GetDnfClauses(LogicManager lm, string name)
    {
        LogicDef def = lm.GetLogicDefStrict(name);
        if (def is not DNFLogicDef dd)
        {
            dd = lm.CreateDNFLogicDef(def.Name, def.ToLogicClause());
        }
        return GetDnfClauses(lm, dd);
    }

    public static List<StatefulClause> GetDnfClauses(LogicManager lm, DNFLogicDef dd)
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
