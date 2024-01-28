namespace APHKLogicExtractor.DataModel
{
    internal record Region(string Name)
    {
        private List<Connection> exits = new();
        public IReadOnlyList<Connection> Exits => exits;

        public HashSet<string> Locations { get; } = new();

        public void Connect(
            IEnumerable<string> requirements,
            IEnumerable<string> locationRequirements,
            IEnumerable<string> stateModifiers,
            Region target)
        {
            RequirementBranch branch = new(
                requirements.ToHashSet(),
                locationRequirements.ToHashSet(),
                stateModifiers.ToList());

            Connection? conn = exits.FirstOrDefault(x => x.Target == target);
            if (conn == null)
            {
                conn = new Connection([branch], target);
                exits.Add(conn);
            }
            else
            {
                conn.Logic.Add(branch);
            }
        }

        public void Disconnect(Region target)
        {
            exits.RemoveAll(x => x.Target == target);
        }
    }
}
