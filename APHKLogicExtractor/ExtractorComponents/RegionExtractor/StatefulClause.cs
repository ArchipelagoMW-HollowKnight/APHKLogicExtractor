using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal class StatefulClause
    {
        private readonly LogicManager lm;

        public TermToken StateProvider { get; }
        public IReadOnlySet<TermToken> Conditions { get; }
        public IReadOnlyList<SimpleToken> StateModifiers { get; }

        public StatefulClause(LogicManager lm, TermToken stateProvider, IReadOnlySet<TermToken> conditions, IReadOnlyList<SimpleToken> stateModifiers)
        {
            this.lm = lm;
            this.StateProvider = stateProvider;
            this.Conditions = conditions;
            this.StateModifiers = stateModifiers;
        }

        /// <summary>
        /// Creates a stateful clause from a term token sequence. Assumed that FALSE has been removed and the clause
        /// has a state provider.
        /// </summary>
        public StatefulClause(LogicManager lm, IEnumerable<TermToken> clause)
        {
            this.lm = lm;
            HashSet<TermToken> conditions = [];
            List<SimpleToken> stateModifiers = [];

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
                        if (v is StateProvider && StateProvider == null)
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
                else
                {
                    conditions.Add(token);
                }
            }

            Conditions = conditions;
            StateModifiers = stateModifiers;
            if (StateProvider == null)
            {
                throw new ArgumentException($"No state-providing token was provided", nameof(clause));
            }
        }

        public IEnumerable<StatefulClause> SubstituteReference(string tokenToSubst, List<StatefulClause> substitution)
        {
            List<TermToken> clause = ToTokens();
            int i = clause.FindIndex(x => x is SimpleToken st && st.Name == tokenToSubst);
            if (i == -1)
            {
                // no substitution needed, keep the clause intact
                yield return new StatefulClause(lm, clause);
            }
            else
            {
                foreach (StatefulClause subst in substitution)
                {
                    List<TermToken> newClause = new(clause);
                    newClause.RemoveAt(i);
                    newClause.InsertRange(i, subst.ToTokens());
                    yield return new StatefulClause(lm, newClause);
                }
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
        /// Creates a new clause representing the substitution of another clause into this clause's state provider.
        /// </summary>
        public StatefulClause SubstituteStateProvider(StatefulClause other)
        {
            TermToken sp = other.StateProvider;
            HashSet<TermToken> conditions = [.. other.Conditions, .. Conditions];
            // order matters here - since we are substituting into the state provider, which is on the left,
            // the substitution's state modifiers must go to the left of our state modifiers
            List<SimpleToken> stateModifiers = [.. other.StateModifiers, .. StateModifiers];
            return new StatefulClause(lm, sp, conditions, stateModifiers);
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
            if (!Conditions.IsSubsetOf(other.Conditions))
            {
                return false;
            }

            // ensure that we provide better state than the other clause.
            int i = 0;
            // state modifiers must all match for the shorter list
            for (; i < StateModifiers.Count && i < other.StateModifiers.Count; i++)
            {
                if (StateModifiers[i] != other.StateModifiers[i])
                {
                    return false;
                }
            }
            if (StateModifiers.Count > other.StateModifiers.Count)
            {
                // in this case, any additional modifiers must be strictly beneficial
                for (; i < StateModifiers.Count; i++)
                {
                    if (classifier.ClassifySingle(StateModifiers[i]) != StateModifierKind.Beneficial)
                    {
                        return false;
                    }
                }
            }
            if (other.StateModifiers.Count > StateModifiers.Count)
            {
                // in this case, any additional modifiers must be strictly detrimental
                for (; i < other.StateModifiers.Count; i++)
                {
                    if (classifier.ClassifySingle(other.StateModifiers[i]) != StateModifierKind.Detrimental)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public List<TermToken> ToTokens()
        {
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
