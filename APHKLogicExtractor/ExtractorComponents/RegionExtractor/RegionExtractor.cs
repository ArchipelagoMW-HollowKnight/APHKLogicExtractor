using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.Loaders;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{

    internal class RegionExtractor(
        ILogger<RegionExtractor> logger,
        IOptions<CommandLineOptions> optionsService,
        DataLoader dataLoader,
        LogicLoader logicLoader
    ) : BackgroundService
    {
        private static FieldInfo pathsField = typeof(DNFLogicDef)
            .GetField("paths", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static Type statePathType = pathsField.FieldType.GetElementType()!;
        private static MethodInfo toTermTokenSequence = statePathType
            .GetMethod("ToTermTokenSequence")!;

        private static Type logicDefBuilder = typeof(LogicManager).Assembly.GetType("RandomizerCore.Logic.DNFLogicDefBuilder", true)!;
        private static MethodInfo createDnfLogicDef = logicDefBuilder.GetMethod("CreateDNFLogicDef")!;

        // todo - consume these as input
        // todo - benchreset and hotspringreset should be default mixed as they are derived from named states
        private static readonly HashSet<string> BeneficialStateModifiers = [
            "$BENCHRESET",
            "$FLOWERGET",
            "$HOTSPRINGRESET",
            "$REGAINSOUL"
        ];
        private static readonly HashSet<string> DetrimentalStateModifiers = [
            "$SHADESKIP",
            "$SPENDSOUL",
            "$TAKEDAMAGE",
            "$EQUIPCHARM",
            "$STAGSTATEMODIFIER"
        ];
        private static readonly HashSet<string> OtherStateModifiers = [
            "$CASTSPELL",
            "$SHRIEKPOGO",
            "$SLOPEBALL",
            "$SAVEQUITRESET",
            "$STARTRESPAWN",
            "$WARPTOBENCH",
            "$WARPTOSTART"
        ];
        // these only set state unconditionally and therefore are completely redundant when appearing in sequence
        private static readonly HashSet<string> StateSetters = [
            "$FLOWERGET",
            "$BENCHRESET",
            "$HOTSPRINGRESET",
            "$SAVEQUITRESET",
            "$STARTRESPAWN",
            "$WARPTOBENCH",
            "$WARPTOSTART"
        ];

        private CommandLineOptions options = optionsService.Value;

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Validating options");
            List<Regex> warpWaypointMatchers = options.WarpWaypoints.Select(w => new Regex($"^{w}$")).ToList();

            logger.LogInformation("Beginning region extraction");

            logger.LogInformation("Fetching data and logic");
            Dictionary<string, RoomDef> roomData = await dataLoader.LoadRooms();
            Dictionary<string, TransitionDef> transitionData = await dataLoader.LoadTransitions();
            Dictionary<string, LocationDef> locationData = await dataLoader.LoadLocations();

            TermCollectionBuilder terms = await logicLoader.LoadTerms();
            RawStateData stateData = await logicLoader.LoadStateFields();
            List<RawLogicDef> transitionLogic = await logicLoader.LoadTransitions();
            List<RawLogicDef> locationLogic = await logicLoader.LoadLocations();
            Dictionary<string, string> macroLogic = await logicLoader.LoadMacros();
            List<RawWaypointDef> waypointLogic = await logicLoader.LoadWaypoints();

            logger.LogInformation("Partitioning waypoints by statefulness");
            Dictionary<string, RawWaypointDef> statefulWaypoints = new();
            Dictionary<string, RawWaypointDef> statelessWaypoints = new();
            Dictionary<string, RawWaypointDef> warpWaypoints = new();

            foreach (RawWaypointDef waypoint in waypointLogic)
            {
                if (warpWaypointMatchers.Any(x => x.IsMatch(waypoint.name)))
                {
                    warpWaypoints[waypoint.name] = waypoint;
                    if (waypoint.stateless)
                    {
                        logger.LogWarning("Manually labeled warp waypoint is not stateful - {}", waypoint.name);
                    }
                    continue;
                }

                Dictionary<string, RawWaypointDef> dict = waypoint.stateless ? statelessWaypoints : statefulWaypoints;
                dict[waypoint.name] = waypoint;
            }

            logger.LogInformation("Solving stateful waypoint logic to macros");
            LogicManagerBuilder preprocessorLmb = new() { VariableResolver = new DummyVariableResolver() };
            preprocessorLmb.LP.SetMacro(macroLogic);
            preprocessorLmb.StateManager.AppendRawStateData(stateData);
            foreach (Term term in terms)
            {
                preprocessorLmb.GetOrAddTerm(term.Name, term.Type);
            }
            foreach (RawWaypointDef wp in waypointLogic)
            {
                preprocessorLmb.AddWaypoint(wp);
            }
            foreach (RawLogicDef transition in transitionLogic)
            {
                preprocessorLmb.AddTransition(transition);
            }

            LogicManager preprocessorLm = new(preprocessorLmb);
            WaypointReferenceGraph waypointReferenceGraph = ConstructWaypointGraph(preprocessorLm, statefulWaypoints);
            IEnumerable<TraversalPath> cyclesToSolve = waypointReferenceGraph.FindCycles()
                .Select(x => x.FindLargestCycleGroup())
                .Distinct(new TraversalPath.Matcher());
            foreach (TraversalPath cycle in cyclesToSolve)
            {
                SubstituteInPath(cycle, preprocessorLm, statefulWaypoints);
            }

            //foreach (TraversalPath path in waypointReferenceGraph.ToPaths())
            //{
            //    SubstituteInPath(path, preprocessorLm, statefulWaypoints);
            //}

            logger.LogInformation("Partitioning logic objects by scene");
            Dictionary<string, IEnumerable<RawLogicDef>> locationsByScene = locationLogic
                .GroupBy(l => locationData[l.name].SceneName)
                .ToDictionary(g => g.Key, g => (IEnumerable<RawLogicDef>)g);
            Dictionary<string, IEnumerable<RawLogicDef>> transitionsByScene = transitionLogic
                .GroupBy(t => transitionData[t.name].SceneName)
                .ToDictionary(g => g.Key, g => (IEnumerable<RawLogicDef>)g);

            logger.LogInformation("Creating regions for scenes");
            IEnumerable<string> scenes = options.Scenes != null ? options.Scenes : roomData.Keys;
            foreach (string scene in scenes)
            {
                logger.LogInformation("Processing scene {}", scene);
            }
        }

        private WaypointReferenceGraph ConstructWaypointGraph(LogicManager lm, Dictionary<string, RawWaypointDef> statefulWaypoints)
        {
            WaypointReferenceGraph graph = new();

            foreach (LogicWaypoint wp in lm.Waypoints)
            {
                if (statefulWaypoints.ContainsKey(wp.Name))
                {
                    List<string> referenceTokens = wp.logic.ToTokenSequence()
                        .OfType<SimpleToken>()
                        .Where(t => statefulWaypoints.ContainsKey(t.Name))
                        .Select(t => t.Name)
                        .ToList();
                    graph.Update(wp.Name, referenceTokens);
                }
            }

            return graph;
        }

        private void SubstituteInPath(TraversalPath path, LogicManager lm, Dictionary<string, RawWaypointDef> statefulWaypoints)
        {
            logger.LogInformation("Performing substitution along path: {}", path.ToString());
            Dictionary<string, List<StatefulClause>> clauses = new();
            // todo - performance improvement, can choose only one node in the path to solve for cycle solving
            foreach (WaypointReferenceNode node in path.DistinctBy(x => x.Name))
            {
                List<StatefulClause> clausesForDef = GetDnfClauses(lm, node.Name);
                clauses[node.Name] = clausesForDef;
                string readable = string.Join(" | ", clausesForDef.Select(clause => clause.ToString()));
                logger.LogInformation("Substitution stage 0 - DNF'd and simplified to clauses for {}: {}", node.Name, readable);
            }

            foreach (WaypointReferenceNode node in path)
            {
                // substitute all non-self-referential waypoints until there are only self-references
                while (true)
                {
                    IEnumerable<string> referencesToSubstitute = clauses[node.Name].SelectMany(x => x.ToTokens())
                        .OfType<SimpleToken>()
                        .Select(x => x.Name)
                        .Where(x => statefulWaypoints.ContainsKey(x) && x != node.Name)
                        .Distinct();
                    if (!referencesToSubstitute.Any())
                    {
                        break;
                    }
                    foreach(string reference in referencesToSubstitute)
                    {
                        clauses[node.Name] = SubstituteInExpression(
                            clauses[node.Name], 
                            reference, 
                            GetDnfClauses(lm, reference)
                        ).ToList();
                    }
                }
                // simplify
                clauses[node.Name] = RemoveRedundantClauses(clauses[node.Name]);
                string readable = string.Join(" | ", clauses[node.Name].Select(clause => clause.ToString()));
                logger.LogInformation("Substitution stage 1 - DNF'd and simplified to clauses for {}: {}", node.Name, readable);
                // substitute all self-referential waypoints, doing self-substitution in accordance to state modifier type
                clauses[node.Name] = SubstituteSelfReferences(lm, clauses[node.Name], node.Name);
                readable = string.Join(" | ", clauses[node.Name].Select(clause => clause.ToString()));
                logger.LogInformation("Substitution stage 2 - DNF'd and simplified to clauses for {}: {}", node.Name, readable);
            }
        }

        private IEnumerable<StatefulClause> SubstituteInExpression(List<StatefulClause> substituteInto, string tokenToSubst, List<StatefulClause> substitution)
        {
            foreach (StatefulClause clause in substituteInto)
            {
                foreach (StatefulClause newClause in clause.SubstituteReference(tokenToSubst, substitution))
                {
                    yield return newClause;
                }
            }
        }

        private List<StatefulClause> SubstituteSelfReferences(LogicManager lm, List<StatefulClause> substituteInto, string tokenToSubst)
        {
            var groupings = substituteInto.GroupBy(x => x.ClassifySelfReferentialityOrThrow(tokenToSubst))
                .ToDictionary(g => g.Key, g => g.ToList());
            List<StatefulClause> nonSelfReferenceClauses = groupings[false];
            foreach (StatefulClause selfReference in groupings[true])
            {
                StateModifierKind kind = ClassifyStateModifiers(selfReference);

                if (kind == StateModifierKind.Mixed)
                {
                    // in this case, we are substituting every self-reference clause into this self-reference clause and
                    // duplicating the original. it is unclear whether the state modifier will improve state, so no shortcuts.
                    List<StatefulClause> newClauses = [];
                    foreach (StatefulClause clause in nonSelfReferenceClauses)
                    {
                        newClauses.Add(ReduceStateModifiersForClause(lm, selfReference.SubstituteStateProvider(clause)));
                    }
                    nonSelfReferenceClauses.AddRange(newClauses);
                }
                else if (kind == StateModifierKind.Beneficial)
                {
                    // in this case, we are essentially doing the same thing as above. However, there is a shortcut we can take:
                    // if the boolean conditions on the non-reference clause are a superset of the conditions on the reference
                    // clause (ie, if the clause we are substituting into has fewer requirements), we can drop the old clause
                    // because the substitution will yield a better state for the same (or less) amount of work
                    List<StatefulClause> newClauses = [];
                    foreach (StatefulClause clause in nonSelfReferenceClauses)
                    {
                        StatefulClause newClause = selfReference.SubstituteStateProvider(clause);
                        newClauses.Add(ReduceStateModifiersForClause(lm, newClause));
                        if (!clause.Conditions.IsSupersetOf(selfReference.Conditions))
                        {
                            newClauses.Add(clause);
                        }
                    }
                    nonSelfReferenceClauses = newClauses;
                }
                // if there were no state modifiers, or if all the state modifiers are strictly detrimental,
                // we can discard the new clause regardless of boolean conditions, because it will get us the
                // same or worse state as the original non-reference clause for the same or more amount of work.

                // Do a simplification pass on the non-self-reference clauses to try and reduce any redundancies
                nonSelfReferenceClauses = RemoveRedundantClauses(nonSelfReferenceClauses);
            }

            return nonSelfReferenceClauses;
        }

        private StateModifierKind ClassifyStateModifiers(StatefulClause clause)
        {
            StateModifierKind kind = StateModifierKind.None;
            foreach (SimpleToken st in clause.StateModifiers)
            {
                string prefix = GetPrefix(st.Name);
                if (BeneficialStateModifiers.Contains(prefix))
                {
                    if (kind == StateModifierKind.None)
                    {
                        kind = StateModifierKind.Beneficial;
                    }
                    else if (kind == StateModifierKind.Detrimental)
                    {
                        kind = StateModifierKind.Mixed;
                    }
                }
                else if (DetrimentalStateModifiers.Contains(prefix))
                {
                    if (kind == StateModifierKind.None)
                    {
                        kind = StateModifierKind.Detrimental;
                    }
                    else if (kind == StateModifierKind.Beneficial)
                    {
                        kind = StateModifierKind.Mixed;
                    }
                }
                else if (OtherStateModifiers.Contains(prefix))
                {
                    kind = StateModifierKind.Mixed;
                }
            }

            return kind;
        }

        private StatefulClause ReduceStateModifiersForClause(LogicManager lm, StatefulClause clause)
        {
            string lastPrefix = "";
            List<SimpleToken> reducedStateModifiers = [];
            foreach (SimpleToken token in clause.StateModifiers)
            {
                string prefix = GetPrefix(token.Name);

                // skip redundant setters
                if (prefix == lastPrefix && StateSetters.Contains(prefix))
                {
                    continue;
                }

                reducedStateModifiers.Add(token);
                lastPrefix = prefix;
            }
            return new StatefulClause(lm, clause.StateProvider, clause.Conditions, reducedStateModifiers);
        }

        private List<StatefulClause> RemoveRedundantClauses(List<StatefulClause> clauses)
        {
            List<StatefulClause> result = new(clauses);
            for (int i = 0; i < result.Count - 1; i++)
            {
                StatefulClause ci = result[i];
                for (int j = i + 1; j < result.Count; j++)
                {
                    StatefulClause cj = result[j];
                    if (ci.IsSameOrBetterThan(cj))
                    {
                        // the left clause is better than the right clause. drop the right clause.
                        // continuing the loop will select the correct right clause next.
                        result.RemoveAt(j);
                        j--;
                    }
                    else if (cj.IsSameOrBetterThan(ci))
                    {
                        // the right clause is better than the left clause. drop the left clause.
                        // this will result in needing to select a new left clause so break out.
                        result.RemoveAt(i);
                        i--;
                        break;
                    }
                }
            }
            return result;
        }

        private List<StatefulClause> GetDnfClauses(LogicManager lm, string name)
        {
            LogicDef def = lm.GetLogicDefStrict(name);
            if (def is not DNFLogicDef dd)
            {
                logger.LogWarning("Logic definition for {} was not available in DNF form, creating", def.Name);
                dd = lm.CreateDNFLogicDef(def.Name, def.ToLogicClause());
            }
            return GetDnfClauses(lm, dd);
        }

        private List<StatefulClause> GetDnfClauses(LogicManager lm, DNFLogicDef dd)
        {
            Array statePaths = (Array)pathsField.GetValue(dd)!;
            List<StatefulClause> clausesForDef = [];
            foreach (object statePath in statePaths)
            {
                IEnumerable<IEnumerable<TermToken>> clausesForPath
                    = (IEnumerable<IEnumerable<TermToken>>)toTermTokenSequence.Invoke(statePath, [])!;
                clausesForDef.AddRange(clausesForPath.Select(x => new StatefulClause(lm, x.ToList())).ToList());
            }
            return clausesForDef;
        }

        private string GetPrefix(string term)
        {
            int x = term.IndexOf('[');
            int y = term.IndexOf(']');
            // various possible problem cases why an arbitrary string might have this operation well-defined.
            // Should not ever come up in theory but better to explode if it did
            if (y < x || (y != -1 && y < term.Length - 1) || x == 0 || (x == -1 && y != -1))
            {
                throw new ArgumentException("Not a valid term to find prefix", nameof(term));
            }
            if (x == -1)
            {
                return term;
            }
            return term[0..x];
        }
    }
}
