namespace APHKLogicExtractor.DataModel
{
    internal record RandomizableTransition(
        string Name,
        string ParentRegion,
        List<RequirementBranch> Logic) : IGraphLogicObject
    {
        public string ParentRegion { get; set; } = ParentRegion;
    }
}
