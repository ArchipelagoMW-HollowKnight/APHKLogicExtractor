namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    /// <summary>
    /// Item effect which applies another effect if the given condition is met
    /// </summary>
    /// <remarks>
    /// Pseudocode for collect:
    /// <code>
    /// if self.condition.satisfied(state) == !self.negated:
    ///   apply_effect(state, self.effect)
    /// </code>
    /// 
    /// Remove is not an easy implementation as we cannot easily determine statically how the applied
    /// effect will or will not affect the value of the condition. Likely, resetting and recomputing affected
    /// terms after removing the item from state is necessary.
    /// </remarks>
    internal record ConditionedEffect(List<RequirementBranch> Condition, bool Negated, IItemEffect Effect) : IItemEffect
    {
        public string Type => "conditional";

        public IReadOnlySet<string> GetAffectedTerms()
        {
            return Effect.GetAffectedTerms();
        }
    }
}
