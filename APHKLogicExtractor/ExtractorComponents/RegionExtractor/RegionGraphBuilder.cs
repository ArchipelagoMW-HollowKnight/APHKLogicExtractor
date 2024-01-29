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
        private Dictionary<string, GraphLocation> locations = new();
        private Dictionary<string, RandomizableTransition> transitions = new();
        private Dictionary<string, HashSet<RandomizableTransition>> transitionsByParentRegion = new();
        public IReadOnlyDictionary<string, Region> Regions => regions;

        public RegionGraphBuilder() 
        {
            AddRegion("Menu");
        }

        public void AddOrUpdateLogicObject(LogicObjectDefinition logicObject)
        {
            Region r = regions.GetValueOrDefault(logicObject.Name) ?? AddRegion(logicObject.Name);
            if (logicObject.Handling == LogicHandling.Location)
            {
                r.Locations.Add(logicObject.Name);
                AddLocation(logicObject.Name);
            }
            RandomizableTransition? t = null;
            if (logicObject.Handling == LogicHandling.Transition)
            {
                t = AddTransition(logicObject.Name);
            }
            foreach (StatefulClause clause in logicObject.Logic)
            {
                string parentName = GetRegionName(clause.StateProvider);
                Region parent = regions.GetValueOrDefault(parentName) ?? AddRegion(parentName);
                var (itemReqs, locationReqs) = PartitionRequirements(clause.Conditions);

                if (logicObject.Handling == LogicHandling.Transition && parent == r)
                {
                    // if this is a self-loop onto a transition region, this isn't a "real" edge,
                    // put the logic on the transition object instead;
                    t!.Logic.Add(new RequirementBranch(
                        itemReqs,
                        locationReqs,
                        clause.StateModifiers.Select(c => c.Write()).ToList()));
                    continue;
                }

                parent.Connect(itemReqs, locationReqs, clause.StateModifiers.Select(c => c.Write()), r);
            }
        }

        public GraphWorldDefinition Build(StateModifierClassifier classfier)
        {
            Validate();
            Clean(classfier);
            return new GraphWorldDefinition(regions.Values, locations.Values, transitions.Values);
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
                foreach (string target in region.Exits.Select(x => x.Target.Name))
                {
                    DotEdge edge = new()
                    {
                        From = new DotIdentifier(region.Name),
                        To = new DotIdentifier(target),
                    };
                    graph.Add(edge);
                }
            }
            return graph;
        }

        private Region AddRegion(string regionName)
        {
            Region r = new(regionName);
            regions.Add(regionName, r);
            return r;
        }

        private GraphLocation AddLocation(string locationName)
        {
            GraphLocation l = new(locationName, [new RequirementBranch([], [], [])]);
            locations.Add(locationName, l);
            return l;
        }

        private RandomizableTransition AddTransition(string transitionName)
        {
            RandomizableTransition t = new(transitionName, transitionName, []);
            transitions.Add(transitionName, t);
            transitionsByParentRegion.Add(transitionName, [t]);
            return t;
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

        private void Validate()
        {
            List<string> aggregatedErrors = [];
            // ensure no locations are duplicated across regions
            IEnumerable<string> duplicated = regions.Values
                .SelectMany(x => x.Locations)
                .GroupBy(x => x)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key);
            if (duplicated.Any())
            {
                aggregatedErrors.Add($"The following locations appeared in multiple regions: {string.Join(", ", duplicated)}");
            }

            // ensure that the declared locations are 1:1 with locations in regions
            HashSet<string> allLocations = regions.Values.SelectMany(x => x.Locations).ToHashSet();
            if (locations.Count != allLocations.Count || !allLocations.IsSupersetOf(locations.Keys))
            {
                allLocations.SymmetricExceptWith(locations.Keys);
                aggregatedErrors.Add($"Expected declared locations to exactly match placed locations, " +
                    $"but the following were not matched: {string.Join(", ", allLocations)}");
            }

            if (aggregatedErrors.Count > 0)
            {
                throw new ValidationException($"One or more validation errors have occured: {string.Join("\n", aggregatedErrors)}");
            }
        }

        private void Clean(StateModifierClassifier classifier)
        {
            RemoveRedundantLogicBranches(classifier);
            while (regions.Values.Any(TryMergeIntoParent) || regions.Values.Any(TryMergeLogicless2Cycle)) 
            {
                RemoveRedundantLogicBranches(classifier);
            }
            foreach (Region region in regions.Values)
            {
                foreach (Connection connection in region.Exits)
                {
                    connection.Logic.RemoveAll(x => x.IsEmpty);
                }
            }
            foreach (GraphLocation location in locations.Values)
            {
                location.Logic.RemoveAll(x => x.IsEmpty);
            }
        }

        private void RemoveRegion(string name)
        {
            Region r = regions[name];
            regions.Remove(name);
            foreach (Region region in r.Parents)
            {
                region.Disconnect(r);
            }
        }

        private bool IsBranchDefinitelyImproved(
            RequirementBranch first,
            RequirementBranch second,
            StateModifierClassifier classifier)
        {
            bool reqsAreSubset = first.ItemRequirements.IsSubsetOf(second.ItemRequirements)
                && first.LocationRequirements.IsSubsetOf(second.LocationRequirements);
            if (!reqsAreSubset)
            {
                return false;
            }

            if (first.StateModifiers.Count >=  second.StateModifiers.Count)
            {
                // if we have more modifiers, anything extra must be definitely positive
                return Utils.HasSublistWithAdditionalModifiersOfKind(
                    first.StateModifiers,
                    second.StateModifiers,
                    classifier,
                    StateModifierKind.Beneficial);
            }
            else
            {
                // if they have more, then all the extra must be definitely negative
                return Utils.HasSublistWithAdditionalModifiersOfKind(
                    second.StateModifiers,
                    first.StateModifiers,
                    classifier,
                    StateModifierKind.Detrimental);
            }
        }

        private void RemoveRedundantLogicBranches(StateModifierClassifier classifier)
        {
            IEnumerable<IGraphLogicObject> logicToReduce = regions.Values
                .SelectMany<Region, IGraphLogicObject>(r => r.Exits)
                .Concat(locations.Values)
                .Concat(transitions.Values);
            foreach (IGraphLogicObject o in logicToReduce)
            {
                for (int i = 0; i < o.Logic.Count - 1; i++)
                {
                    RequirementBranch li = o.Logic[i];
                    for (int j = i + 1; j < o.Logic.Count; j++)
                    {
                        RequirementBranch lj = o.Logic[j];

                        if (IsBranchDefinitelyImproved(lj, li, classifier))
                        {
                            // the right clause is better than the left clause. drop the left clause.
                            // this will result in needing to select a new left clause so break out.
                            o.Logic.RemoveAt(i);
                            i--;
                            break;
                        }
                        else if (IsBranchDefinitelyImproved(li, lj, classifier))
                        {
                            // the left clause is better than the right clause. drop the right clause.
                            // continuing the loop will select the correct right clause next.
                            o.Logic.RemoveAt(j);
                            j--;
                        }
                    }
                }
            }
        }

        private bool TryMergeIntoParent(Region child)
        {
            // if the region has any exits it is not safe to merge destructively in this manner
            if (child.Exits.Count != 0)
            {
                return false;
            }

            // needs a single parent to have a well-defined merge
            IReadOnlySet<Region> parents = child.Parents;
            if (parents.Count != 1)
            {
                return false;
            }

            Region parent = parents.First();
            Connection conn = parent.Exits.Where(x => x.Target == child).First();

            // Disconnect our region from the graph
            RemoveRegion(child.Name);
            // pull the location into the parent region and prepend all the edge logic
            // to the location logic on the child node
            foreach (string location in child.Locations)
            {
                parent.Locations.Add(location);

                GraphLocation l = locations[location];
                List<RequirementBranch> newBranches = [];
                foreach (RequirementBranch cl in conn.Logic)
                {
                    foreach (RequirementBranch ll in l.Logic)
                    {
                        newBranches.Add(cl + ll);
                    }
                }
                if (conn.Logic.Count > 0)
                {
                    l.Logic.Clear();
                    l.Logic.AddRange(newBranches);
                }
            }
            // if this region is a randomizable transition, pull it up a level and prepend edge logic
            if (transitionsByParentRegion.TryGetValue(child.Name, out HashSet<RandomizableTransition>? ts))
            {
                foreach (RandomizableTransition t in ts)
                {
                    t.ParentRegion = parent.Name;
                    List<RequirementBranch> newBranches = [];
                    foreach (RequirementBranch cl in conn.Logic)
                    {
                        foreach (RequirementBranch tl in t.Logic)
                        {
                            newBranches.Add(cl + tl);
                        }
                    }
                    if (conn.Logic.Count == 0)
                    {
                        t.Logic.Clear();
                        t.Logic.AddRange(newBranches);
                    }
                }
                HashSet<RandomizableTransition> addTo = transitionsByParentRegion.GetValueOrDefault(parent.Name, []);
                addTo.UnionWith(ts);
                transitionsByParentRegion[parent.Name] = addTo;
                transitionsByParentRegion.Remove(child.Name);
            }
            return true;
        }

        private bool TryMergeLogicless2Cycle(Region parent)
        {
            IEnumerable<Region> absorbableRegions = parent.Exits
                .Where(edge => edge.Logic.All(branch => branch.IsEmpty))
                .Select(edge => edge.Target)
                .Where(target => target != parent && target.Exits
                    .Any(backEdge => backEdge.Logic.All(branch => branch.IsEmpty) && backEdge.Target == parent));
            if (!absorbableRegions.Any())
            {
                return false;
            }

            // we have successfully found one or more 2-cycles which has no requirements in either direction.
            // that means these regions are topologically equivalent, so we can absorb all the contents of the
            // other region(s) and delete them. To avoid modifying while iterating we will solve one at a time.
            Region child = absorbableRegions.First();
            parent.Locations.UnionWith(child.Locations);
            // need a copy so we don't modify while iterating
            foreach (Connection childExit in child.Exits.Where(x => x.Target != parent).ToList())
            {
                parent.Connect(childExit.Logic, childExit.Target);
                child.Disconnect(childExit.Target);
            }
            foreach (Region otherParent in child.Parents.Where(p => p != parent))
            {
                // get the link to the child
                Connection conn = otherParent.Exits.First(x => x.Target == child);
                // disconnect from the child
                otherParent.Disconnect(child);
                // connect to this
                otherParent.Connect(conn.Logic, parent);
            }
            if (transitionsByParentRegion.TryGetValue(child.Name, out HashSet<RandomizableTransition>? ts))
            {
                foreach (RandomizableTransition t in ts)
                {
                    t.ParentRegion = parent.Name;
                }
                HashSet<RandomizableTransition> addTo = transitionsByParentRegion.GetValueOrDefault(parent.Name, []);
                addTo.UnionWith(ts);
                transitionsByParentRegion[parent.Name] = addTo;
                transitionsByParentRegion.Remove(child.Name);
            }
            RemoveRegion(child.Name);
            return true;
        }
    }
}
