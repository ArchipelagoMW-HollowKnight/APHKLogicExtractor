namespace APHKLogicExtractor.DataModel.ItemExtractor
{
    internal record ConditionedEffect(List<RequirementBranch> Condition, bool Negated, IItemEffect Effect) : IItemEffect
    {
        public string Type => "conditional";
    }
}
