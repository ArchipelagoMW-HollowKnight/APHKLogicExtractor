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
        LogicLoader logicLoader,
        TermPrefixParser prefixParser,
        StateModifierClassifier stateClassifier
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
                SolveCyclicPath(cycle, preprocessorLm, statefulWaypoints);
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

        private void SolveCyclicPath(TraversalPath path, LogicManager lm, Dictionary<string, RawWaypointDef> statefulWaypoints)
        {
            logger.LogInformation("Performing substitution along path: {}", path.ToString());
            Dictionary<string, List<StatefulClause>> clauses = new();
            // get all referenced logic as well as we may need to solve for that too
            Queue<string> processingQueue = new(path.Select(x => x.Name).Distinct());
            while (processingQueue.TryDequeue(out string? next))
            {
                if (clauses.ContainsKey(next))
                {
                    continue;
                }

                List<StatefulClause> clausesForDef = GetDnfClauses(lm, next);
                clauses[next] = clausesForDef;
                string readable = string.Join(" | ", clausesForDef.Select(clause => clause.ToString()));
                logger.LogInformation("Substitution stage 0 - DNF'd and simplified to clauses for {}: {}", next, readable);

                IEnumerable<string> unprocessedReferences = GetNonSelfReferences(clauses[next], next, statefulWaypoints)
                    .Where(x => !clauses.ContainsKey(x));
                foreach (string reference in unprocessedReferences)
                {
                    processingQueue.Enqueue(reference);
                }
            }

            // goal: reduce all logic references one at a time in sequence. This can be accomplished by performing substitutions

            foreach (WaypointReferenceNode node in path)
            {
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

        private IEnumerable<string> GetNonSelfReferences(List<StatefulClause> clauses, string name, Dictionary<string, RawWaypointDef> statefulWaypoints)
        {
            return clauses.SelectMany(x => x.ToTokens())
                .OfType<SimpleToken>()
                .Select(x => x.Name)
                .Where(x => statefulWaypoints.ContainsKey(x) && x != name)
                .Distinct();
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
                StateModifierKind kind = stateClassifier.ClassifyMany(selfReference.StateModifiers);

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

        private StatefulClause ReduceStateModifiersForClause(LogicManager lm, StatefulClause clause)
        {
            string lastPrefix = "";
            List<SimpleToken> reducedStateModifiers = [];
            foreach (SimpleToken token in clause.StateModifiers)
            {
                string prefix = prefixParser.GetPrefix(token.Name);

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
                    if (ci.IsSameOrBetterThan(cj, stateClassifier))
                    {
                        // the left clause is better than the right clause. drop the right clause.
                        // continuing the loop will select the correct right clause next.
                        result.RemoveAt(j);
                        j--;
                    }
                    else if (cj.IsSameOrBetterThan(ci, stateClassifier))
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
    }
}
