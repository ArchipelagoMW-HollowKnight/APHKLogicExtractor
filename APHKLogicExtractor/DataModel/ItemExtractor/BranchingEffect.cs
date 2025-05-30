﻿namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    /// <summary>
    /// Item effect which represents an "if-else" chain, applying the appropriate effect based on
    /// which nested conditions were satisfied.
    /// </summary>
    /// <remarks>
    /// Pseudocode for collect:
    /// <code>
    /// for conditional in self.conditionals:
    ///   if apply_effect(state, conditional):
    ///     break
    /// else:
    ///   if self.else:
    ///     apply_effect(state, self.else)
    /// </code>
    /// 
    /// Remove is not an easy implementation as we cannot easily determine statically how the applied
    /// effect will or will not affect the value of the condition. Likely, resetting and recomputing affected
    /// terms after removing the item from state is necessary.
    /// </remarks>
    internal record BranchingEffect : IItemEffect
    {
        public string Type => "branching";

        public IReadOnlySet<string> GetAffectedTerms()
        {
            return Conditionals
            .SelectMany(x => x.GetAffectedTerms())
            .Concat(Else?.GetAffectedTerms() ?? new HashSet<string>())
            .ToHashSet();
        }

        public IReadOnlyList<ConditionedEffect> Conditionals { get; }

        public IItemEffect? Else { get; }

        public BranchingEffect(IReadOnlyList<IItemEffect> effects)
        {
            List<ConditionedEffect> conditionals = [];
            foreach (IItemEffect effect in effects)
            {
                IItemEffect? simplified = effect.Simplify();
                if (simplified is ConditionedEffect ce)
                {
                    conditionals.Add(ce);
                }
                // if we see any effect which is not conditional, nothing after that matters
                // as it will always have an effect and therefore short-circuit
                else if (simplified != null)
                {
                    if (simplified is not (MultiEffect or IncrementTermsEffect))
                    {
                        throw new NotImplementedException("Does not properly handle nested branching effects");
                    }
                    Else = simplified;
                    break;
                }
            }
            Conditionals = conditionals;
        }

        public IItemEffect? Simplify()
        {
            if (Conditionals.Count == 0)
            {
                return Else;
            }
            // most of our simplification is done on construction, the only thing we cannot
            // do there is change type
            return ThresholdEffect.TryConvert(this);
        }
    }
}
