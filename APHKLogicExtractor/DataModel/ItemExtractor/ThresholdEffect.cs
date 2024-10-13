namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    /// <summary>
    /// A special case of branching items to handle items of a form like "if TERM&lt;THRESHOLD, TERM++ and other effects, else TERM++"
    /// and similar, such as HK charms.
    /// </summary>
    /// <remarks>
    /// Pseudocode for collect:
    /// <code>
    /// if state.terms[self.term] &lt; self.threshold:
    ///   # see incrementTerms effect
    ///   increment_terms(self.below_threshold)
    /// elif state.terms[self.term] &gt; self.threshold:
    ///   increment_terms(self.above_threshold)
    /// else:
    ///   increment_terms(self.at_threshold)
    /// state.terms[self.term] += 1
    /// </code>
    /// 
    /// Pseudocode for remove:
    /// <code>
    /// state.terms[self.term] -= 1
    /// if state.terms[self.term] &lt; self.threshold:
    ///   # see incrementTerms effect
    ///   decrement_terms(self.below_threshold)
    /// elif state.terms[self.term] &gt; self.threshold:
    ///   decrement_terms(self.above_threshold)
    /// else:
    ///   decrement_terms(self.at_threshold)
    /// </code>
    /// </remarks>
    internal record ThresholdEffect(string Term, int Threshold, 
        IReadOnlyDictionary<string, int> BelowThreshold, 
        IReadOnlyDictionary<string, int> AtThreshold,
        IReadOnlyDictionary<string, int> AboveThreshold) : IItemEffect
    {
        public string Type => "threshold";

        public IReadOnlySet<string> GetAffectedTerms()
        {
            return BelowThreshold.Keys
            .Concat(AtThreshold.Keys)
            .Concat(AboveThreshold.Keys)
            .Append(Term)
            .ToHashSet();
        }

        public static IItemEffect TryConvert(BranchingEffect be)
        {
            if (be.Conditionals.Count == 1 && be.Conditionals[0].Condition.Count == 1
                && be.Conditionals[0].Condition[0].ItemRequirements.Count == 1
                && be.Conditionals[0].Condition[0].LocationRequirements.Count == 0
                && be.Conditionals[0].Condition[0].RegionRequirements.Count == 0
                && be.Conditionals[0].Condition[0].StateModifiers.Count == 0)
            {
                string rawReq = be.Conditionals[0].Condition[0].ItemRequirements.First();
                string[] req = rawReq.Split('<', '=', '>').Select(x => x.Trim()).ToArray();
                if (req.Length != 2 || !int.TryParse(req[1], out int threshold))
                {
                    return be;
                }

                string term = req[0];
                if (be.Conditionals[0].Effect is IncrementTermsEffect e1
                    && be.Else is IncrementTermsEffect e2
                    && e1.Effects.TryGetValue(term, out int v1) && v1 == 1
                    && e2.Effects.TryGetValue(term, out int v2) && v2 == 1)
                {
                    Dictionary<string, int>? metEffects = new(e1.Effects);
                    metEffects.Remove(term);

                    Dictionary<string, int>? notMetEffects = new(e2.Effects);
                    notMetEffects.Remove(term);

                    // handle effect arrangement.
                    if (rawReq.IndexOf('<') >= 0)
                    {
                        if (be.Conditionals[0].Negated)
                        {
                            // !T<X == T>=X
                            return new ThresholdEffect(term, threshold, notMetEffects, metEffects, metEffects);
                        }
                        else
                        {
                            return new ThresholdEffect(term, threshold, metEffects, notMetEffects, notMetEffects);
                        }
                    }
                    else if (rawReq.IndexOf('>') >= 0)
                    {
                        if (be.Conditionals[0].Negated)
                        {
                            // !T>X == T<=X
                            return new ThresholdEffect(term, threshold, metEffects, metEffects, notMetEffects);
                        }
                        else
                        {
                            return new ThresholdEffect(term, threshold, notMetEffects, notMetEffects, metEffects);
                        }
                    }
                    else
                    {
                        if (be.Conditionals[0].Negated)
                        {
                            // !T=X == T!=X
                            return new ThresholdEffect(term, threshold, metEffects, notMetEffects, metEffects);
                        }
                        else
                        {
                            return new ThresholdEffect(term, threshold, notMetEffects, metEffects, notMetEffects);
                        }
                    }
                }
                else
                {
                    return be;
                }
            }
            else
            {
                return be;
            }
        }
    }
}
