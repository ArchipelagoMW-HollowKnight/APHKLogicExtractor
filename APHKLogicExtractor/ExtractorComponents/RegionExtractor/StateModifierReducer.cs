using RandomizerCore.Logic;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal class StateModifierReducer(VariableParser parser)
    {
        // todo - consume these as input
        // these only set state unconditionally and therefore are completely redundant when appearing in sequence
        private static readonly HashSet<string> StateSetters = [
            "$FLOWERGET",
            "$BENCHRESET",
            "$HOTSPRINGRESET",
            "$SAVEQUITRESET",
            "$STARTRESPAWN",
            "$WARPTOBENCH",
            "$WARPTOSTART"
        ];

        public StatefulClause? ReduceStateModifiers(LogicManager lm, StatefulClause clause)
        {
            List<SimpleToken> modifiers = [.. clause.StateModifiers];
            for (int i = 0; i < modifiers.Count; i++)
            {
                string prefix = parser.GetPrefix(modifiers[i].Name);

                // if 2 identical setters appear immediately in sequence they are redundant
                while (i + 1 < modifiers.Count && parser.GetPrefix(modifiers[i + 1].Name) == prefix)
                {
                    modifiers.RemoveAt(i + 1);
                }

                // todo - encode domain specific handling better

                // shade skips without a bench reset between them are redundant
                if (prefix == "$SHADESKIP")
                {
                    for (int j = i + 1; j < modifiers.Count; j++)
                    {
                        if (parser.GetPrefix(modifiers[j].Name) == prefix)
                        {
                            return null;
                        }
                        if (parser.GetPrefix(modifiers[j].Name) == "$BENCHRESET")
                        {
                            break;
                        }
                    }
                }

                // sequences of benchreset + hotspringreset can be reduced to a single pair 
                if (i + 1 < modifiers.Count)
                {
                    string next = parser.GetPrefix(modifiers[i + 1].Name);
                    // we know that redundant setters were removed so we can be sure that these 2 will appear adjacent to each other
                    // in the cases we can reduce like this

                    if (prefix == "$BENCHRESET" && next == "$HOTSPRINGRESET" || prefix == "$HOTSPRINGRESET" && next == "$BENCHRESET")
                    {
                        while (i + 2 < modifiers.Count)
                        {
                            string nextNext = parser.GetPrefix(modifiers[i + 2].Name);
                            if (nextNext != "$BENCHRESET" && nextNext != "$HOTSPRINGRESET")
                            {
                                break;
                            }
                            else
                            {
                                modifiers.RemoveAt(i + 2);
                            }
                        }
                        if (prefix == "$HOTSPRINGRESET")
                        {
                            // these can commute for consistent ordering in comparisons
                            SimpleToken temp = modifiers[i];
                            modifiers[i] = modifiers[i + 1];
                            modifiers[i + 1] = temp;
                        }
                    }
                }
            }


            // assume that some arbitrarily long sequence of modifiers after other reductions
            // is incompletable or redundant.
            // It does not reproduce logic in full fidelity but does allow
            // the completion of the program so it is a worthwhile tradeoff
            if (modifiers.Count > 10)
            {
                return null;
            }

            return new StatefulClause(lm, clause.StateProvider, clause.Conditions, modifiers);
        }

        public List<StatefulClause> ReduceStateModifiers(LogicManager lm, List<StatefulClause> clauses)
        {
            return clauses.Select(x => ReduceStateModifiers(lm, x)).Where(x => x != null).ToList()!;
        }
    }
}
