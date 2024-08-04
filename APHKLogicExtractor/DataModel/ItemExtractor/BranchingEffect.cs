namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal record BranchingEffect : IItemEffect
    {
        public string Type => "branching";

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
