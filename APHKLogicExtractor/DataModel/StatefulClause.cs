using APHKLogicExtractor.ExtractorComponents.RegionExtractor;
using Newtonsoft.Json;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.DataModel
{
    internal class StatefulClause
    {
        public TermToken? StateProvider { get; }
        public IReadOnlySet<TermToken> Conditions { get; }
        public IReadOnlyList<TermToken> StateModifiers { get; }

        private TermToken ParseSingleToken(string token)
        {
            List<LogicToken> tokens = Infix.Tokenize(token);
            if (tokens.Count != 1 || tokens[0] is not TermToken tt)
            {
                throw new ArgumentException($"Logic string {token} must consist of a single TermToken.", nameof(token));
            }
            return tt;
        }

        [JsonConstructor]
        private StatefulClause(
            [JsonProperty(PropertyName = "StateProvider")] string? stateProvider,
            [JsonProperty(PropertyName = "Conditions")] IEnumerable<string> conditions,
            [JsonProperty(PropertyName = "StateModifiers")] IEnumerable<string> stateModifiers)
        {
            if (stateProvider != null)
            {
                StateProvider = ParseSingleToken(stateProvider);
            }
            Conditions = conditions.Select(ParseSingleToken).ToHashSet();
            StateModifiers = stateModifiers.Select(ParseSingleToken).ToList();
            if (StateProvider == null && StateModifiers.Count > 0)
            {
                throw new ArgumentException($"No state-providing token was provided", nameof(stateProvider));
            }
        }

        public StatefulClause(TermToken? stateProvider, IReadOnlySet<TermToken> conditions, IReadOnlyList<TermToken> stateModifiers)
        {
            StateProvider = stateProvider;
            Conditions = conditions;
            StateModifiers = stateModifiers;
            if (StateProvider == null && StateModifiers.Count > 0)
            {
                throw new ArgumentException($"No state-providing token was provided", nameof(stateProvider));
            }
        }

        /// <summary>
        /// Creates a stateful clause from a term token sequence. Assumed that FALSE has been removed and the clause
        /// has a state provider.
        /// </summary>
        public StatefulClause(LogicManager lm, IEnumerable<TermToken> clause)
        {
            HashSet<TermToken> conditions = [];
            List<TermToken> stateModifiers = [];

            foreach (TermToken token in clause)
            {
                if (token is SimpleToken st)
                {
                    if (lm.GetTerm(st.Name) is Term t)
                    {
                        if (t.Type == TermType.State && StateProvider == null)
                        {
                            StateProvider = token;
                        }
                        else
                        {
                            conditions.Add(token);
                        }
                    }
                    else if (lm.GetVariable(st.Name) is LogicVariable v)
                    {
                        if (v is IStateProvider && StateProvider == null)
                        {
                            StateProvider = token;
                        }
                        else if (v is StateModifier)
                        {
                            stateModifiers.Add(st);
                        }
                        else
                        {
                            conditions.Add(token);
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"A token of an unknown type was provided: {token}", nameof(clause));
                    }
                }
                else if (token is ReferenceToken)
                {
                    if (StateProvider == null)
                    {
                        StateProvider = token;
                    }
                    else
                    {
                        conditions.Add(token);
                    }
                }
                else if (token is ComparisonToken ct)
                {
                    if (lm.GetVariable(ct.Left) is StateAccessVariable || lm.GetVariable(ct.Right) is StateAccessVariable)
                    {
                        stateModifiers.Add(ct);
                    }
                    else
                    {
                        conditions.Add(ct);
                    }
                }
                else
                {
                    conditions.Add(token);
                }
            }

            Conditions = conditions;
            StateModifiers = stateModifiers;
            if (StateProvider == null && StateModifiers.Count > 0)
            {
                throw new ArgumentException($"No state-providing token was provided", nameof(clause));
            }
        }

        /// <summary>
        /// Validates that the clause is either not self-referential (contains no references to name), or that
        /// it is self-referential and that the only reference is the state provider. If neither are met, an exception
        /// is thrown.
        /// </summary>
        /// <returns>Whether the clause is self-referential.</returns>
        public bool ClassifySelfReferentialityOrThrow(string name)
        {
            foreach (TermToken tt in Conditions)
            {
                if (tt is SimpleToken st && st.Name == name)
                {
                    throw new InvalidOperationException($"Unexpected boolean self-reference to {name} in {this}");
                }
                if (tt is ComparisonToken ct && (ct.Left == name || ct.Right == name))
                {
                    throw new InvalidOperationException($"Unexpected comparison self-reference to {name} in {this}");
                }
            }
            return StateProvider is SimpleToken sp && sp.Name == name;
        }

        /// <summary>
        /// Determines whether this clause is an equivalent or better clause, that is, whether it will yield a state which is at least
        /// as good as the other clause and has the same or fewer conditions on obtaining that state.
        /// </summary>
        public bool IsSameOrBetterThan(StatefulClause other, StateModifierClassifier classifier)
        {
            // if the state provider is different this is already not a meaningful comparison.
            if (StateProvider != other.StateProvider)
            {
                return false;
            }

            // ensure that we have better conditions (no more than) the other clause
            if (!other.Conditions.IsSupersetOf(Conditions))
            {
                return false;
            }

            // ensure that we provide better state than the other clause.
            if (StateModifiers.Count >= other.StateModifiers.Count)
            {
                // if we have more modifiers, they must all be good for this to be definitively better
                // we can also use this case to check equality if lists are the same length as the sublist should
                // still be found with no extras
                return HasSublistWithAdditionalModifiersOfKind(StateModifiers, other.StateModifiers, classifier, StateModifierKind.Beneficial);
            }
            else
            {
                // if the other clause has more modifiers, then they must all be bad for this to be definitively better
                return HasSublistWithAdditionalModifiersOfKind(other.StateModifiers, StateModifiers, classifier, StateModifierKind.Detrimental);
            }
        }

        private bool HasSublistWithAdditionalModifiersOfKind(
            IReadOnlyList<TermToken> list,
            IReadOnlyList<TermToken> sublist,
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

        public List<TermToken> ToTokens()
        {
            if (StateProvider == null)
            {
                return [
                    .. Conditions,
                    .. StateModifiers
                ];
            }

            return [
                StateProvider,
                .. Conditions,
                .. StateModifiers
            ];
        }

        public override string ToString()
        {
            string inner = string.Join(" + ", ToTokens().Select(x => x.Write()));
            return $"({inner})";
        }
    }
}
