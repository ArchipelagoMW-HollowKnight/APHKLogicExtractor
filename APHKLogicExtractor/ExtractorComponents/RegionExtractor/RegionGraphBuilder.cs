﻿using APHKLogicExtractor.DataModel;
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
        private HashSet<string> transitions = new();
        private Dictionary<string, HashSet<Region>> parentLookup = new();
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
            if (logicObject.Handling == LogicHandling.Transition)
            {
                transitions.Add(logicObject.Name);
            }
            foreach (StatefulClause clause in logicObject.Logic)
            {
                string parentName = GetRegionName(clause.StateProvider);
                Region parent = regions.GetValueOrDefault(parentName) ?? AddRegion(parentName);
                var (itemReqs, locationReqs) = PartitionRequirements(clause.Conditions);
                parent.Connect(itemReqs, locationReqs, clause.StateModifiers.Select(c => c.Write()), r);

                if (!parentLookup.TryGetValue(logicObject.Name, out HashSet<Region>? parents))
                {
                    parents = new HashSet<Region>();
                    parentLookup[logicObject.Name] = parents;
                }
                parents.Add(parent);
            }
        }

        public GraphWorldDefinition Build(StateModifierClassifier classfier)
        {
            Validate();
            Clean(classfier);
            return new GraphWorldDefinition(regions.Values, locations.Values);
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
            while (regions.Values.Any(TryMergeIntoParent)) 
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
            foreach (Region region in parentLookup.GetValueOrDefault(name) ?? Enumerable.Empty<Region>())
            {
                region.Disconnect(r);
            }
            parentLookup.Remove(name);
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
                .Concat(locations.Values);
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
            // if the region has no exits it is not safe to merge destructively in this manner
            // if it a transition it needs to be preserved for ER.
            if (child.Exits.Count != 0 || transitions.Contains(child.Name))
            {
                return false;
            }

            // needs a single parent to have a well-defined merge
            IReadOnlySet<Region> parents = parentLookup.GetValueOrDefault(child.Name, []);
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
                l.Logic.Clear();
                l.Logic.AddRange(newBranches);
            }
            return true;
        }
    }
}
