namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal record MultiEffect(IReadOnlyList<IItemEffect> Effects) : IItemEffect
    {
        public string Type => "multiple";

        public IReadOnlySet<string> GetAffectedTerms()
        {
            return Effects.SelectMany(x => x.GetAffectedTerms()).ToHashSet();
        }

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
                // distribute term effects down to the leaf nodes
                IncrementTermsEffect incrementTerms = new(composedTermEffects);
                // but if there's nothing to distribute across then we should just return the new effect
                if (simplifiedEffects.Count == 0)
                {
                    return incrementTerms;
                }
                simplifiedEffects = simplifiedEffects
                    .Select(e => Distribute(e, incrementTerms).Simplify())
                    .Where(e => e != null)
                    .ToList()!;
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

        // precondition: incr is non-empty ie will do something.
        private IItemEffect Distribute(IItemEffect effect, IncrementTermsEffect incr)
        {
            if (effect is MultiEffect me)
            {
                return new MultiEffect([.. me.Effects, incr]).Simplify() 
                    ?? throw new NullReferenceException("Expected non-null result from Multi+Increment simplify");
            }
            else if (effect is BranchingEffect be)
            {
                List<ConditionedEffect> conditions = be.Conditionals
                    .Select(ce => new ConditionedEffect(ce.Condition, ce.Negated, Distribute(ce.Effect, incr)))
                    .ToList();
                IItemEffect @else = be.Else switch
                {
                    IItemEffect e => Distribute(e, incr),
                    _ => incr,
                };
                return new BranchingEffect([.. conditions, @else]);
            }
            else if (effect is ConditionedEffect ce)
            {
                // can convert this to "if condition, effect + incr, else incr"
                return new BranchingEffect([new ConditionedEffect(ce.Condition, ce.Negated, Distribute(ce.Effect, incr)), incr]);
            }
            else
            {
                return new MultiEffect([effect, incr]).Simplify()
                    ?? throw new NullReferenceException("Expected non-null result from Multi+Increment simplify");
            }
        }
    }
}
