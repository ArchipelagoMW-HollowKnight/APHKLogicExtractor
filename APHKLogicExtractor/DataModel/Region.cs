using Newtonsoft.Json;

namespace APHKLogicExtractor.DataModel
{
    internal record Region(string Name)
    {
        private HashSet<Region> parents = new();
        [JsonIgnore]
        public IReadOnlySet<Region> Parents => parents;

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

            Connect([branch], target);
        }

        public void Connect(List<RequirementBranch> branches, Region target)
        {
            Connection? conn = exits.FirstOrDefault(x => x.Target == target);
            if (conn == null)
            {
                conn = new Connection(branches, target);
                exits.Add(conn);
                target.parents.Add(this);
            }
            else
            {
                conn.Logic.AddRange(branches);
            }
        }

        public void Disconnect(Region target)
        {
            target.parents.Remove(this);
            exits.RemoveAll(x => x.Target == target);
        }
    }
}
