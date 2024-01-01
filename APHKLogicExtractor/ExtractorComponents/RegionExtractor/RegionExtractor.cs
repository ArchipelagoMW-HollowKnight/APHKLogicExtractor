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
        StateModifierClassifier stateClassifier,
        StateModifierReducer reducer
    ) : BackgroundService
    {
        private static FieldInfo pathsField = typeof(DNFLogicDef)
            .GetField("paths", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static Type statePathType = pathsField.FieldType.GetElementType()!;
        private static MethodInfo toTermTokenSequence = statePathType
            .GetMethod("ToTermTokenSequence")!;

        private static Type logicDefBuilder = typeof(LogicManager).Assembly.GetType("RandomizerCore.Logic.DNFLogicDefBuilder", true)!;
        private static MethodInfo createDnfLogicDef = logicDefBuilder.GetMethod("CreateDNFLogicDef")!;

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
            Queue<string> processingQueue = new();
            processingQueue.Enqueue(path.First().Name);
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

            while (clauses.Values.Any(clause => HasAnyReferencesTo(clause, clauses.Keys)))
            {
                foreach (string reference in clauses.Keys)
                {
                    // first substitute any self-references that are already present so that we can safely
                    // substitute this into other expressions
                    clauses[reference] = SubstituteSelfReferences(lm, clauses[reference], reference);
                    foreach (string other in clauses.Keys.Where(x => x != reference))
                    {
                        clauses[other] = SubstituteInExpression(lm, clauses[other], reference, clauses[reference]);
                    }
                }
            }

            string clauseCounts = string.Join("\n", clauses.Select(x => $"* {x.Key}: {x.Value.Count}"));
            logger.LogInformation("Solved cycle {}, clause counts are:\n{}", path.ToString(), clauseCounts);
        }

        private IEnumerable<string> GetNonSelfReferences(IEnumerable<StatefulClause> clauses, string name, Dictionary<string, RawWaypointDef> statefulWaypoints)
        {
            return clauses.SelectMany(x => x.ToTokens())
                .OfType<SimpleToken>()
                .Select(x => x.Name)
                .Where(x => statefulWaypoints.ContainsKey(x) && x != name)
                .Distinct();
        }

        private bool HasAnyReferencesTo(IEnumerable<StatefulClause> clauses, IReadOnlyCollection<string> references)
        {
            return clauses.SelectMany(x => x.ToTokens())
                .OfType<SimpleToken>()
                .Select(x => x.Name)
                .Any(references.Contains);
        }

        private List<StatefulClause> SubstituteInExpression(LogicManager lm, 
            List<StatefulClause> substituteInto, string tokenToSubst, List<StatefulClause> substitution)
        {
            List<StatefulClause> result = [];
            foreach (StatefulClause clause in substituteInto)
            {
                foreach (StatefulClause newClause in clause.SubstituteReference(tokenToSubst, substitution))
                {
                    StatefulClause? reduced = reducer.ReduceStateModifiers(lm, newClause);
                    if (reduced != null)
                    {
                        result.Add(reduced);
                    }
                }
            }
            return RemoveRedundantClauses(result);
        }

        private List<StatefulClause> SubstituteSelfReferences(LogicManager lm, List<StatefulClause> substituteInto, string tokenToSubst)
        {
            var groupings = substituteInto.GroupBy(x => x.ClassifySelfReferentialityOrThrow(tokenToSubst))
                .ToDictionary(g => g.Key, g => g.ToList());
            List<StatefulClause> nonSelfReferenceClauses = groupings.GetValueOrDefault(false, []);
            foreach (StatefulClause selfReference in groupings.GetValueOrDefault(true, []))
            {
                StateModifierKind kind = stateClassifier.ClassifyMany(selfReference.StateModifiers);

                if (kind == StateModifierKind.Mixed)
                {
                    // in this case, we are substituting every self-reference clause into this self-reference clause and
                    // duplicating the original. it is unclear whether the state modifier will improve state, so no shortcuts.
                    List<StatefulClause> newClauses = [];
                    foreach (StatefulClause clause in nonSelfReferenceClauses)
                    {
                        StatefulClause newClause = selfReference.SubstituteStateProvider(clause);
                        StatefulClause? reduced = reducer.ReduceStateModifiers(lm, newClause);
                        if (reduced != null)
                        {
                            newClauses.Add(reduced);
                        }
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
                        StatefulClause? reduced = reducer.ReduceStateModifiers(lm, newClause);
                        if (reduced != null)
                        {
                            newClauses.Add(reduced);
                        }
                        if (reduced == null || !clause.Conditions.IsSupersetOf(selfReference.Conditions))
                        {
                            // if the new clause is actually impossible, or if the conditions are not improved,
                            // we cannot drop the old clause
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

        private List<StatefulClause> RemoveRedundantClauses(List<StatefulClause> clauses)
        {
            List<StatefulClause> result = new(clauses);
            for (int i = 0; i < result.Count - 1; i++)
            {
                StatefulClause ci = result[i];
                for (int j = i + 1; j < result.Count; j++)
                {
                    StatefulClause cj = result[j];
                    if (cj.IsSameOrBetterThan(ci, stateClassifier))
                    {
                        // the right clause is better than the left clause. drop the left clause.
                        // this will result in needing to select a new left clause so break out.
                        result.RemoveAt(i);
                        i--;
                        break;
                    }
                    else if (ci.IsSameOrBetterThan(cj, stateClassifier))
                    {
                        // the left clause is better than the right clause. drop the right clause.
                        // continuing the loop will select the correct right clause next.
                        result.RemoveAt(j);
                        j--;
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
