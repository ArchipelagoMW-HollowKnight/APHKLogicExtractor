using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal record WaypointReferenceNode(string Name, ICollection<WaypointReferenceNode> References, ICollection<WaypointReferenceNode> Referrers);

    internal class TraversalPath : IEnumerable<WaypointReferenceNode>
    {
        internal class Matcher : IEqualityComparer<TraversalPath>
        {
            public bool Equals(TraversalPath? x, TraversalPath? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }
                else if (x != null && y != null)
                {
                    return x.path.Count == y.path.Count && Enumerable.Range(0, x.path.Count).All(i => x.path[i].Name == y.path[i].Name);
                }
                else
                {
                    return false;
                }
            }

            public int GetHashCode([DisallowNull] TraversalPath obj)
            {
                unchecked
                {
                    int hash = 19;
                    foreach (WaypointReferenceNode node in obj.path)
                    {
                        hash = hash * 31 + node.Name.GetHashCode();
                    }
                    return hash;
                }
            }
        }

        private readonly List<WaypointReferenceNode> path = [];

        public int Length => path.Count;

        public TraversalPath(WaypointReferenceNode start)
        {
            path.Add(start);
        }

        private TraversalPath(TraversalPath orig, WaypointReferenceNode next)
        {
            path.AddRange(orig.path);
            path.Add(next);
        }

        private TraversalPath(IEnumerable<WaypointReferenceNode> path)
        {
            this.path.AddRange(path);
        }

        public bool IsCycle => path.Count > 1 && path.DistinctBy(x => x.Name).Count() < path.Count;

        public bool IsComplete => path[^1].References.Count == 0;

        public IEnumerable<TraversalPath> Step()
        {
            foreach (WaypointReferenceNode reference in path[^1].References)
            {
                yield return new TraversalPath(this, reference);
            }
        }

        public TraversalPath FindLargestCycleGroup()
        {
            if (!IsCycle)
            {
                throw new InvalidOperationException();
            }

            for (int i = 0; i < path.Count; i++)
            {
                if (path[i].Name == path[^1].Name)
                {
                    return new TraversalPath(path[i..^1]);
                }
            }
            throw new InvalidOperationException();
        }

        public override string ToString() => string.Join(" -> ", path.Select(x => x.Name));

        public IEnumerator<WaypointReferenceNode> GetEnumerator() => path.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal class WaypointReferenceGraph
    {
        private readonly Dictionary<string, WaypointReferenceNode> members = new();
        public ICollection<WaypointReferenceNode> Roots { get; } = new HashSet<WaypointReferenceNode>();

        public void Update(string name, IEnumerable<string> references)
        {
            if (!members.TryGetValue(name, out WaypointReferenceNode? node))
            {
                node = new WaypointReferenceNode(name, new List<WaypointReferenceNode>(), new List<WaypointReferenceNode>());
                // a new node with no references is assumed to be root
                Roots.Add(node);
                members.Add(name, node);
            }
            // make new references
            node.References.Clear();

            foreach (string reference in references)
            {
                if (members.TryGetValue(reference, out WaypointReferenceNode? referenceNode))
                {
                    // if this is an existing node it is not a root anymore because something is referencing it
                    Roots.Remove(referenceNode);
                }
                else
                {
                    referenceNode = new(reference, new List<WaypointReferenceNode>(), new List<WaypointReferenceNode>());
                    members.Add(reference, referenceNode);
                }

                node.References.Add(referenceNode);
                referenceNode.Referrers.Add(node);
            }
        }

        public WaypointReferenceGraph Inverse()
        {
            WaypointReferenceGraph inverse = new();

            foreach (WaypointReferenceNode node in members.Values)
            {
                inverse.Update(node.Name, node.Referrers.Select(x => x.Name));
            }
            return inverse;
        }

        public IEnumerable<TraversalPath> ToPaths()
        {
            List<TraversalPath> paths = [.. Roots.Select(x => new TraversalPath(x))];
            for (int i = 0; i < paths.Count; i++)
            {
                TraversalPath path = paths[i];
                if (path.IsCycle || path.IsComplete)
                {
                    if (path.Length == 1)
                    {
                        paths.RemoveAt(i);
                        i--;
                    }
                    continue;
                }
                paths.RemoveAt(i);
                paths.InsertRange(i, path.Step());
                i--;
            }
            return paths.Distinct(new TraversalPath.Matcher());
        }

        public IEnumerable<TraversalPath> FindCycles()
        {
            return ToPaths().Where(p => p.IsCycle);
        }
    }
}
