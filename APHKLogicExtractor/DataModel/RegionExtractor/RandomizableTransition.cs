namespace APHKLogicExtractor.DataModel.RegionExtractor
{
    internal record RandomizableTransition(
        string Name,
        List<RequirementBranch> Logic) : IGraphLogicObject;
}
