namespace APHKLogicExtractor.DataModel
{
    internal record RandomizableTransition(
        string Name,
        List<RequirementBranch> Logic) : IGraphLogicObject;
}
