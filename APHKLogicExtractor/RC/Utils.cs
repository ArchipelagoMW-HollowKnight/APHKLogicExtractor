using RandomizerCore.Logic;

namespace APHKLogicExtractor.RC;

public class Utils
{
    public static TermCollectionBuilder assembleTerms(Dictionary<string, List<string>> terms)
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
}
