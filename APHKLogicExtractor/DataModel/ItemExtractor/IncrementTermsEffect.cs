namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    /// <summary>
    /// Item effect which applies one or more term effects
    /// </summary>
    /// <remarks>
    /// Pseudocode for collect:
    /// <code>
    /// for term, amount in self.effects.items():
    ///   state.terms[term] += amount
    /// </code>
    /// 
    /// Pseudocode for remove:
    /// <code>
    /// for term, amount in self.effects.items():
    ///   state.terms[term] -= amount
    /// </code>
    /// </remarks>
    internal record IncrementTermsEffect(IReadOnlyDictionary<string, int> Effects) : IItemEffect
    {
        public string Type => "incrementTerms";

        public IReadOnlySet<string> GetAffectedTerms()
        {
            return Effects.Keys.ToHashSet();
        }
    }
}
