namespace APHKLogicExtractor.DataModel
{
    internal record GraphLocation(string Name, List<RequirementBranch> Logic, bool IsEvent) : IGraphLogicObject;
}
