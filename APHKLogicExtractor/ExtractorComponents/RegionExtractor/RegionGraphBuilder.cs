using APHKLogicExtractor.DataModel;
using DotNetGraph.Core;
using DotNetGraph.Extensions;
using RandomizerCore.Logic;
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
        public IReadOnlyDictionary<string, Region> Regions => regions;

        public RegionGraphBuilder()
        {
            AddRegion("Menu");
        }

        public void AddOrUpdateLogicObject(LogicObjectDefinition logicObject, LogicManager? lm)
        {
            Region r = regions.GetValueOrDefault(logicObject.Name) ?? AddRegion(logicObject.Name);
            if (logicObject.Handling == LogicHandling.Location)
            {
                r.Locations.Add(logicObject.Name);
                AddLocation(logicObject.Name, logicObject.IsEventLocation);
            }
            RandomizableTransition? t = null;
            if (logicObject.Handling == LogicHandling.Transition)
            {
                r.Transitions.Add(logicObject.Name);
                t = AddTransition(logicObject.Name);
            }
            foreach (StatefulClause clause in logicObject.Logic)
            {
                string parentName = GetRegionName(clause.StateProvider);
                Region parent = regions.GetValueOrDefault(parentName) ?? AddRegion(parentName);
                var (itemReqs, locationReqs, regionReqs) = PartitionRequirements(clause.Conditions, lm);

                if (logicObject.Handling == LogicHandling.Transition && parent == r)
                {
                    // if this is a self-loop onto a transition region, this isn't a "real" edge,
                    // put the logic on the transition object instead;
                    t!.Logic.Add(new RequirementBranch(
                        itemReqs,
                        locationReqs,
                        regionReqs,
                        clause.StateModifiers.Select(c => c.Write()).ToList()));
                    continue;
                }

                parent.Connect(itemReqs, locationReqs, regionReqs, clause.StateModifiers.Select(c => c.Write()), r);
            }
        }

        public void LabelRegionAsMenu(string regionName)
        {
            Region r = regions[regionName];
            Region menu = regions["Menu"];

            // If we're doing this, it's to rebase state propagation onto the Menu region,
            // so the declared region should not have any locations, exits, or transitions
            if (r.Exits.Count > 0 || r.Locations.Count > 0 || r.Transitions.Count > 0)
            {
                throw new InvalidOperationException("Should not merge state region into menu with any child objects");
            }
            // reconnect all parents to the menu region instead
            // make a copy to avoid modification during iteration
            foreach (Region parent in r.Parents.ToList())
            {
                Connection conn = parent.Exits.First(x => x.Target == r);
                parent.Disconnect(r);
                parent.Connect(conn.Logic, menu);
            }
            RemoveRegion(regionName);
        }

        public GraphWorldDefinition Build(StateModifierClassifier classifier, IReadOnlySet<string>? regionsToKeep)
        {
            Validate();
            Clean(classifier, regionsToKeep);
            Dictionary<string, string> transitionToRegionMap = new();
            foreach (Region r in regions.Values)
            {
                foreach (string t in r.Transitions)
                {
                    transitionToRegionMap[t] = r.Name;
                }
            }
            return new GraphWorldDefinition(regions.Values, locations.Values, transitions.Values, transitionToRegionMap);
        }

        public DotGraph BuildDotGraph()
        {
            DotGraph graph = new DotGraph().WithIdentifier("Regions").Directed();
            foreach (Region region in regions.Values)
            {
                string htmlLabel = $"""
                    <table border="0" cellborder="1" cellspacing="0">
                        <tr><td><b>{region.Name}</b></td></tr>
                        <tr><td>{string.Join("<br/>", region.Locations)}</td></tr>
                        <tr><td>{string.Join("<br/>", region.Transitions)}</td></tr>
                    </table>
                    """;
                DotNode node = new DotNode()
                    .WithIdentifier(region.Name)
                    .WithShape(DotNodeShape.PlainText)
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

        private GraphLocation AddLocation(string locationName, bool isEvent)
        {
            GraphLocation l = new(locationName, [new RequirementBranch([], [], [], [])], isEvent);
            locations.Add(locationName, l);
            return l;
        }

        private RandomizableTransition AddTransition(string transitionName)
        {
            RandomizableTransition t = new(transitionName, []);
            transitions.Add(transitionName, t);
            return t;
        }

        private string GetRegionName(TermToken? token) => token switch
        {
            null => "Menu",
            SimpleToken st => st.Name,
            ReferenceToken rt => rt.Target,
            _ => throw new InvalidOperationException($"Tokens of type {token.GetType().FullName} are not valid region parents")
        };

        private (HashSet<string> itemReqs, HashSet<string> locationReqs, HashSet<string> regionReqs) PartitionRequirements(
            IEnumerable<TermToken> reqs,
            LogicManager? lm
        )
        {
            HashSet<string> items = new HashSet<string>();
            HashSet<string> locations = new HashSet<string>();
            HashSet<string> regions = new HashSet<string>();
            foreach (TermToken t in reqs)
            {
                if (t is ReferenceToken rt)
                {
                    locations.Add(rt.Target);
                }
                else if (t is ProjectedToken pt)
                {
                    if (pt.Inner is ReferenceToken rtt)
                    {
                        locations.Add(rtt.Target);
                    }
                    else
                    {
                        regions.Add(pt.Inner.Write());
                    }
                }
                else
                {
                    // bug workaround - at the time of writing, projection tokens are flattened by RC
                    // so we have to semantically check our item requirements to see if they should have been projected
                    string token = t.Write();
                    if (lm != null && lm.GetTransition(token) != null)
                    {
                        regions.Add(token);
                    }
                    else if (lm != null && lm.Waypoints.Any(w => w.Name == token && w.term.Type == TermType.State))
                    {
                        regions.Add(token);
                    }
                    else 
                    {
                        items.Add(token);
                    }
                }
            }
            return (items, locations, regions);
        }

        private void Validate()
        {
            List<string> aggregatedErrors = [];
            // ensure no transitions are duplicated across regions
            IEnumerable<string> duplicatedTransitions = regions.Values
                .SelectMany(x => x.Transitions)
                .GroupBy(x => x)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key);
            if (duplicatedTransitions.Any())
            {
                aggregatedErrors.Add($"The following transitions appeared in multiple regions: {string.Join(", ", duplicatedTransitions)}");
            }

            // ensure that the declared transitions are 1:1 with transitions in regions
            HashSet<string> allTransitions = regions.Values.SelectMany(x => x.Transitions).ToHashSet();
            if (transitions.Count != allTransitions.Count || !allTransitions.IsSupersetOf(transitions.Keys))
            {
                allTransitions.SymmetricExceptWith(transitions.Keys);
                aggregatedErrors.Add($"Expected declared transitions to exactly match placed transitions, " +
                    $"but the following were not matched: {string.Join(", ", allTransitions)}");
            }

            // ensure no locations are duplicated across regions
            IEnumerable<string> duplicatedLocactions = regions.Values
                .SelectMany(x => x.Locations)
                .GroupBy(x => x)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key);
            if (duplicatedLocactions.Any())
            {
                aggregatedErrors.Add($"The following locations appeared in multiple regions: {string.Join(", ", duplicatedLocactions)}");
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

        private void Clean(StateModifierClassifier classifier, IReadOnlySet<string>? regionsToKeep)
        {
            HashSet<string> protectedRegions = [];
            if (regionsToKeep != null)
            {
                protectedRegions.UnionWith(regionsToKeep);
            }
            foreach (GraphLocation l in locations.Values)
            {
                foreach (RequirementBranch b in l.Logic)
                {
                    protectedRegions.UnionWith(b.RegionRequirements);
                }
            }
            // also protect any regions which were referenced by region requirements
            foreach (RandomizableTransition t in transitions.Values)
            {
                foreach (RequirementBranch b in t.Logic)
                {
                    protectedRegions.UnionWith(b.RegionRequirements);
                }
            }

            RemoveRedundantLogicBranches(classifier);
            while (regions.Values.Any(x => TryMergeIntoParent(x, protectedRegions))
                || regions.Values.Any(x => TryMergeLogicless2Cycle(x, protectedRegions))
                || regions.Values.Any(x => TryRemoveEmptyRegion(x, classifier, protectedRegions)))
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
            foreach (Connection exit in r.Exits.ToList())
            {
                r.Disconnect(exit.Target);
            }
        }

        private bool IsBranchDefinitelyImproved(
            RequirementBranch first,
            RequirementBranch second,
            StateModifierClassifier classifier)
        {
            bool reqsAreSubset = first.ItemRequirements.IsSubsetOf(second.ItemRequirements)
                && first.LocationRequirements.IsSubsetOf(second.LocationRequirements)
                && first.RegionRequirements.IsSubsetOf(second.RegionRequirements);
            if (!reqsAreSubset)
            {
                return false;
            }

            if (first.StateModifiers.Count >= second.StateModifiers.Count)
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

        private bool TryMergeIntoParent(Region child, IReadOnlySet<string> regionsToKeep)
        {
            // do not merge me if I'm protected
            if (regionsToKeep.Contains(child.Name))
            {
                return false;
            }

            // if the region has any exits or transitions, it is not safe to merge in this manner
            if (child.Exits.Count != 0 || child.Transitions.Count != 0)
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
                List<RequirementBranch> newBranches = DistributeBranches(conn.Logic, l.Logic);
                l.Logic.Clear();
                l.Logic.AddRange(newBranches);
            }
            // pull randomizable transitions into the parent region and prepend edge logic
            // to the transition logic on the child node
            foreach (string transition in child.Transitions)
            {
                parent.Transitions.Add(transition);

                RandomizableTransition t = transitions[transition];
                List<RequirementBranch> newBranches = DistributeBranches(conn.Logic, t.Logic);
                t.Logic.Clear();
                t.Logic.AddRange(newBranches);
            }
            return true;
        }

        private bool TryMergeLogicless2Cycle(Region parent, IReadOnlySet<string> regionsToKeep)
        {
            IEnumerable<Region> absorbableRegions = parent.Exits
                .Where(edge => edge.Logic.All(branch => branch.IsEmpty))
                .Select(edge => edge.Target)
                // doesn't matter how edible the child region looks if we are not allowed to eat it
                // merging stuff into protected parents is ok though.
                .Where(target => !regionsToKeep.Contains(target.Name))
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
            parent.Transitions.UnionWith(child.Transitions);
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
            RemoveRegion(child.Name);
            return true;
        }

        private bool TryRemoveEmptyRegion(Region region, StateModifierClassifier classifier, IReadOnlySet<string> regionsToKeep)
        {
            // attempts to remove a placeholder region (no locations or transitions) by distributing incoming edges
            // across outgoing edges. As such, the region must have both incoming and outgoing edges.
            if (region.Locations.Any() || region.Transitions.Any() || !region.Parents.Any())
            {
                return false;
            }

            // if a region has a self-cycle (assumed to be state-modifying) it cannot be removed safely.
            if (region.Parents.Contains(region))
            {
                return false;
            }

            if (regionsToKeep.Contains(region.Name))
            {
                return false;
            }

            // if it's a total dead end just kill it and move on
            if (!region.Exits.Any())
            {
                RemoveRegion(region.Name);
                return true;
            }

            IEnumerable<(Region, Connection)> entrances = region.Parents
                .SelectMany(parent => parent.Exits.Select(conn => (parent, conn)))
                .Where(entrance => entrance.conn.Target == region)
                .ToList();
            // need a copy to avoid modification during iteration
            IEnumerable<Connection> exits = region.Exits.ToList();
            // axe this region
            RemoveRegion(region.Name);

            // surely this will have no negative performance implications
            foreach (var (parent, entrance) in entrances)
            {
                foreach (var exit in exits)
                {
                    List<RequirementBranch> newBranches = DistributeBranches(entrance.Logic, exit.Logic);
                    // in a self-cycle, any non-state-modifying branches are redundant (you cannot get to yourself without yourself).
                    // strictly worse state modifiers are also redundant
                    if (parent == exit.Target)
                    {
                        newBranches.RemoveAll(x => x.StateModifiers.Count == 0
                            || classifier.ClassifyMany(x.StateModifiers) == StateModifierKind.Detrimental);
                        if (!newBranches.Any())
                        {
                            continue;
                        }
                    }
                    parent.Connect(newBranches, exit.Target);
                }
            }
            return true;
        }

        private List<RequirementBranch> DistributeBranches(List<RequirementBranch> left, List<RequirementBranch> right)
        {
            List<RequirementBranch> newBranches;
            if (left.Count > 0 && right.Count > 0)
            {
                newBranches = [];
                foreach (RequirementBranch l1 in left)
                {
                    foreach (RequirementBranch l2 in right)
                    {
                        newBranches.Add(l1 + l2);
                    }
                }
            }
            else if (left.Count == 0)
            {
                newBranches = right;
            }
            else
            {
                newBranches = right;
            }
            return newBranches;
        }
    }
}
