using Newtonsoft.Json;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;
using RandomizerCore;

namespace APHKLogicExtractor.DataModel
{
    internal class StatefulClause : IEquatable<StatefulClause>
    {
        public Expr? StateProvider { get; }
        public IReadOnlySet<Expr> Conditions { get; }
        public IReadOnlyList<Expr> StateModifiers { get; }

        [JsonConstructor]
        private StatefulClause(
            [JsonProperty(PropertyName = "StateProvider")] string? stateProvider,
            [JsonProperty(PropertyName = "Conditions")] IEnumerable<string> conditions,
            [JsonProperty(PropertyName = "StateModifiers")] IEnumerable<string> stateModifiers)
        {
            if (stateProvider != null)
            {
                StateProvider = LogicExpressionUtil.Parse(stateProvider);
            }
            Conditions = conditions.Select(LogicExpressionUtil.Parse).ToHashSet();
            StateModifiers = [.. stateModifiers.Select(LogicExpressionUtil.Parse)];
            if (StateProvider == null && StateModifiers.Count > 0)
            {
                throw new ArgumentException($"No state-providing token was provided", nameof(stateProvider));
            }
        }

        public StatefulClause(Expr? stateProvider, IReadOnlySet<Expr> conditions, IReadOnlyList<Expr> stateModifiers)
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
        /// Creates a stateful clause from a ReadOnlyConjunction
        /// has a state provider.
        /// </summary>
        public StatefulClause(LogicManager lm, DNFLogicDef.ReadOnlyConjunction clause)
        {
            HashSet<Expr> conditions = [];
            List<Expr> stateModifiers = [];

            StateProvider = clause.StateProvider?.ToExpression();
            foreach (TermValue termReq in clause.TermReqs)
            {
                conditions.Add(termReq.ToExpression());
            }
            foreach (LogicInt varReq in clause.VarReqs)
            {
                conditions.Add(varReq.ToExpression());
            }
            foreach (StateModifier modifier in clause.StateModifiers)
            {
                stateModifiers.Add(modifier.ToExpression());
            }

            Conditions = conditions;
            StateModifiers = stateModifiers;
            if (StateProvider == null && StateModifiers.Count > 0)
            {
                throw new ArgumentException($"No state-providing token was provided", nameof(clause));
            }
        }

        public Expr ToExpression()
        {
            List<Expr> parts;
            if (StateProvider == null)
            {
                parts = [
                    .. Conditions,
                    .. StateModifiers
                ];
            }
            else
            {
                parts = [
                    StateProvider,
                .. Conditions,
                .. StateModifiers
                ];
            }
            LogicExpressionBuilder builder = new();
            return builder.ApplyInfixOperatorLeftAssoc(parts.DefaultIfEmpty(builder.NameAtom("NONE")), builder.Op("+"));
        }

        public override string ToString()
        {
            return $"({ToExpression().Print()})";
        }

        public (HashSet<string> itemReqs, HashSet<string> locationReqs, HashSet<string> regionReqs)
            PartitionRequirements(LogicManager? lm)
        {
            HashSet<string> items = new HashSet<string>();
            HashSet<string> locations = new HashSet<string>();
            HashSet<string> regions = new HashSet<string>();
            foreach (Expr expr in Conditions)
            {
                switch (expr)
                {
                    case ReferenceExpression { Operand: Atom a }:
                        locations.Add(a.Token.Print());
                        break;
                    case ProjectionExpression { Operand: ReferenceExpression { Operand: Atom a } }:
                        locations.Add(a.Token.Print());
                        break;
                    case ProjectionExpression { Operand: Atom a }:
                        regions.Add(a.Token.Print());
                        break;
                    case Atom a:
                        items.Add(a.Token.Print());
                        break;
                    case ComparisonExpression { Left: Atom, Right: Atom } ce:
                        items.Add(ce.Print());
                        break;
                    default:
                        throw new ArgumentException("Unsupported expression type");
                }
            }
            return (items, locations, regions);
        }

        public bool Equals(StatefulClause? other)
        {
            if (other == null)
            {
                return false;
            }

            if (StateProvider != other.StateProvider)
            {
                return false;
            }

            if (Conditions.Count != other.Conditions.Count)
            {
                return false;
            }
            foreach (Expr cond in Conditions)
            {
                if (!other.Conditions.Contains(cond))
                {
                    return false;
                }
            }

            if (StateModifiers.Count != other.StateModifiers.Count)
            {
                return false;
            }
            for (int i = 0; i < StateModifiers.Count; i++)
            {
                if (StateModifiers[i] != other.StateModifiers[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
