namespace APHKLogicExtractor.DataModel
{
    internal record Region(string Name)
    {
        public record Connection(HashSet<string> Requirements, Region Target);

        private List<Connection> exits = new();
        public IReadOnlyList<Connection> Exits => exits;

        public HashSet<string> Locations { get; } = new();

        public void Connect(IEnumerable<string> requirements, Region target)
        {
            exits.Add(new Connection(requirements.ToHashSet(), target));
        }
    }
}
