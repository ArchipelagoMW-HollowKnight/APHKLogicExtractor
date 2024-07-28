namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal interface IItemEffect
    {
        string Type { get; }

        IItemEffect? Simplify() => this;
    }

    internal record IncrementTermsEffect(Dictionary<string, int> Effects) : IItemEffect
    {
        public string Type => "incrementTerms";
    }

    internal record ConditionedEffect(List<RequirementBranch> Condition, bool Negated, IItemEffect Effect) : IItemEffect
    {
        public string Type => "conditional";
    }

    internal record MultiEffect(List<IItemEffect> Effects) : IItemEffect
    {
        public string Type => "multiple";

        public IItemEffect? Simplify()
        {
            Dictionary<string, int> composedTermEffects = new();
            List<IItemEffect> simplifiedEffects = [];
            foreach (IItemEffect effect in Effects)
            {
                IItemEffect? simple = effect.Simplify();
                if (simple is IncrementTermsEffect te)
                {
                    foreach (var (term, amount) in te.Effects)
                    {
                        if (!composedTermEffects.ContainsKey(term))
                        {
                            composedTermEffects[term] = 0;
                        }
                        composedTermEffects[term] += amount;
                    }
                }
                else if (simple != null)
                {
                    simplifiedEffects.Add(simple);
                }
            }

            if (composedTermEffects.Count > 0)
            {
                IncrementTermsEffect incrementTerms = new(composedTermEffects);
                simplifiedEffects.Add(incrementTerms);
            }

            if (simplifiedEffects.Count > 1)
            {
                return new MultiEffect(simplifiedEffects);
            }
            else if (simplifiedEffects.Count == 1)
            {
                return simplifiedEffects[0];
            }
            else
            {
                return null;
            }
        }
    }

    internal record BranchingEffect : IItemEffect
    {
        public string Type => "branching";

        public List<ConditionedEffect> Conditionals { get; }

        public IItemEffect? Else { get; }

        public BranchingEffect(List<IItemEffect> effects)
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
            return this;
        }
    }
}
