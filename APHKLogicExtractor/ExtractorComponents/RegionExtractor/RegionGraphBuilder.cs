using APHKLogicExtractor.DataModel;
using DotNetGraph.Core;
using DotNetGraph.Extensions;
using RandomizerCore.StringLogic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal class RegionGraphBuilder
    {
        private Dictionary<string, Region> regions = new();
        private Dictionary<string, List<Region>> parentLookup = new();
        public IReadOnlyDictionary<string, Region> Regions => regions.ToImmutableDictionary();

        public RegionGraphBuilder() 
        {
            AddRegion("Menu");        
        }

        private Region AddRegion(string regionName)
        {
            Region r = new(regionName);
            regions.Add(regionName, r);
            return r;
        }

        private string GetRegionName(TermToken? token) => token switch
        {
            null => "Menu",
            SimpleToken st => st.Name,
            ReferenceToken rt => rt.Target,
            _ => throw new InvalidOperationException($"Tokens of type {token.GetType().FullName} are not valid region parents")
        };

        private (HashSet<string> itemReqs, HashSet<string> locationReqs) PartitionRequirements(IEnumerable<TermToken> reqs)
        {
            HashSet<string> items = new HashSet<string>();
            HashSet<string> locations = new HashSet<string>();
            void Partition(TermToken t)
            {
                if (t is ReferenceToken rt)
                {
                    locations.Add(rt.Target);
                }
                else if (t is ProjectedToken pt)
                {
                    Partition(pt.Inner);
                }
                else
                {
                    items.Add(t.Write());
                }
            }
            foreach (TermToken t in reqs)
            {
                Partition(t);
            }
            return (items, locations);
        }

        public void AddOrUpdateLogicObject(LogicObjectDefinition logicObject, bool isLocation)
        {
            Region r = regions.GetValueOrDefault(logicObject.Name) ?? AddRegion(logicObject.Name);
            if (isLocation)
            {
                r.Locations.Add(logicObject.Name);
            }
            foreach (StatefulClause clause in logicObject.Logic)
            {
                string parentName = GetRegionName(clause.StateProvider);
                Region parent = regions.GetValueOrDefault(parentName) ?? AddRegion(parentName);
                var (itemReqs, locationReqs) = PartitionRequirements(clause.Conditions);
                parent.Connect(itemReqs, locationReqs, clause.StateModifiers.Select(c => c.Write()), r);
                
                if (!parentLookup.TryGetValue(logicObject.Name, out List<Region>? parents))
                {
                    parents = new List<Region>();
                    parentLookup[logicObject.Name] = parents;
                }
                parents.Add(parent);
            }
        }

        public void RemoveRegion(string name)
        {
            Region r = regions[name];
            regions.Remove(name);
            foreach (Region region in parentLookup.GetValueOrDefault(name) ?? Enumerable.Empty<Region>())
            {
                region.Disconnect(r);
            }
            parentLookup.Remove(name);
        }

        public IReadOnlyList<Region> GetParents(string regionName) => parentLookup[regionName];

        public void Validate()
        {
            List<string> aggregatedErrors = [];
            // ensure no locations are duplicated
            IEnumerable<string> duplicated = regions.Values
                .SelectMany(x => x.Locations)
                .GroupBy(x => x)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key);
            if (duplicated.Any())
            {
                aggregatedErrors.Add($"The following locations appeared in multiple regions: {string.Join(", ", duplicated)}");
            }

            if (aggregatedErrors.Count > 0)
            {
                throw new ValidationException($"One or more validation errors have occured: {string.Join("\n", aggregatedErrors)}");
            }
        }

        public List<Region> Build()
        {
            return regions.Values.ToList();
        }

        public DotGraph BuildDotGraph()
        {
            DotGraph graph = new DotGraph().WithIdentifier("Graph").Directed();
            foreach (Region region in regions.Values)
            {
                string htmlLabel = $"<b>{region.Name}</b>";
                foreach (string loc in region.Locations)
                {
                    htmlLabel += $"<br/>{loc}";
                }
                DotNode node = new DotNode()
                    .WithIdentifier(region.Name)
                    .WithShape(DotNodeShape.Box)
                    .WithLabel(htmlLabel, true);
                graph.Add(node);
                foreach (string target in region.Exits.Select(x => x.Target.Name).Distinct())
                {
                    DotEdge edge = new DotEdge()
                    {
                        From = new DotIdentifier(region.Name),
                        To = new DotIdentifier(target),
                    };
                    graph.Add(edge);
                }
            }
            return graph;
        }
    }
}
