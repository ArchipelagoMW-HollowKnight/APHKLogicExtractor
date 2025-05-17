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

        [JsonConstructor]
        private StatefulClause(
            [JsonProperty(PropertyName = "StateProvider")] string? stateProvider,
            [JsonProperty(PropertyName = "Conditions")] IEnumerable<string> conditions,
            [JsonProperty(PropertyName = "StateModifiers")] IEnumerable<string> stateModifiers)
        {
            if (stateProvider != null)
            {
                StateProvider = Utils.ParseSingleToken(stateProvider);
            }
            Conditions = conditions.Select(Utils.ParseSingleToken).ToHashSet();
            StateModifiers = stateModifiers.Select(Utils.ParseSingleToken).ToList();
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



        public (HashSet<string> itemReqs, HashSet<string> locationReqs, HashSet<string> regionReqs)
            PartitionRequirements(LogicManager? lm)
        {
            HashSet<string> items = new HashSet<string>();
            HashSet<string> locations = new HashSet<string>();
            HashSet<string> regions = new HashSet<string>();
            foreach (TermToken t in Conditions)
            {
                if (t is ReferenceToken rt)
                {
                    locations.Add(rt.Target);
                }
                else if (t is ProjectedToken pt)
                {
                    if (pt.Inner is ReferenceToken rtt)
                    {
                        locations.Add(rtt.Target);
                    }
                    else
                    {
                        regions.Add(pt.Inner.Write());
                    }
                }
                else
                {
                    // bug workaround - at the time of writing, projection tokens are flattened by RC
                    // so we have to semantically check our item requirements to see if they should have been projected
                    string token = t.Write();
                    if (lm != null && lm.GetTransition(token) != null)
                    {
                        regions.Add(token);
                    }
                    else if (lm != null && lm.Waypoints.Any(w => w.Name == token && w.term.Type == TermType.State))
                    {
                        regions.Add(token);
                    }
                    else
                    {
                        items.Add(token);
                    }
                }
            }
            return (items, locations, regions);
        }
    }
}
