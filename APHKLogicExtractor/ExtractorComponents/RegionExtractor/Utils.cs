using APHKLogicExtractor.DataModel;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal static class Utils
    {
        public static TermToken ParseSingleToken(string token)
        {
            List<LogicToken> tokens = Infix.Tokenize(token);
            if (tokens.Count != 1 || tokens[0] is not TermToken tt)
            {
                throw new ArgumentException($"Logic string {token} must consist of a single TermToken.", nameof(token));
            }
            return tt;
        }

        public static bool HasSublistWithAdditionalModifiersOfKind(
            IReadOnlyList<string> list,
            IReadOnlyList<string> sublist,
            StateModifierClassifier classifier,
            StateModifierKind kind)
        {
            if (!list.ToHashSet().IsSupersetOf(sublist))
            {
                return false;
            }

            int i = 0;
            for (; i + sublist.Count <= list.Count; i++)
            {
                int j = 0;
                for (; j < sublist.Count; j++)
                {
                    if (list[i + j] != sublist[j])
                    {
                        // assuming that the sublist check will pass, then the first element we checked is extra.
                        if (classifier.ClassifySingle(list[i]) != kind)
                        {
                            return false;
                        }
                        break;
                    }
                }
                // the whole sublist was matched, check the rest of the list
                if (j == sublist.Count)
                {
                    for (int k = i + j; k < list.Count; k++)
                    {
                        if (classifier.ClassifySingle(list[k]) != kind)
                        {
                            return false;
                        }
                    }
                    // all the classifications passed and the sublist matched so we are good
                    return true;
                }
            }
            // we never matched the sublist
            return false;
        }
    }
}
