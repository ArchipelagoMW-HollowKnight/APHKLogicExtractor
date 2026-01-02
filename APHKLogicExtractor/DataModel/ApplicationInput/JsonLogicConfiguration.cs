using APHKLogicExtractor.DataModel.RandomizerData;
using APHKLogicExtractor.Loader;
using Newtonsoft.Json.Linq;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringItems;
using RandomizerCore.StringLogic;
using RandomizerCore.StringParsing;

namespace APHKLogicExtractor.DataModel;

internal record JsonLogicConfiguration
{
    public JsonData? Data { get; set; }
    public JsonLogic? Logic { get; set; }

    public static async Task<List<JsonLogicConfiguration>> ParseManyAsync(MaybeFile<JToken> configFile)
    {
        List<JsonLogicConfiguration> configs = await configFile.GetContent<List<JsonLogicConfiguration>>();
        List<JsonLogicConfiguration> finalConfigs = [];
        foreach (JsonLogicConfiguration conf in configs)
        {
            if (conf.Logic == null || (conf.Logic.Include == null && conf.Logic.Exclude == null))
            {
                finalConfigs.Add(conf);
                continue;
            }

            List<RawWaypointDef> waypoints = [];
            if (conf.Logic.Waypoints != null)
            {
                waypoints = await conf.Logic.Waypoints.GetContent();
            }
            Dictionary<string, RawWaypointDef> waypointLookup = waypoints.ToDictionary(w => w.name);

            List<RawLogicDef> locations = [];
            if (conf.Logic.Locations != null)
            {
                locations = await conf.Logic.Locations.GetContent();
            }
            Dictionary<string, RawLogicDef> locationLookup = locations.ToDictionary(l => l.name);

            List<RawLogicDef> transitions = [];
            if (conf.Logic.Transitions != null)
            {
                transitions = await conf.Logic.Transitions.GetContent();
            }
            Dictionary<string, RawLogicDef> transitionsLookup = transitions.ToDictionary(t => t.name);

            HashSet<string> allNames = [.. waypointLookup.Keys, .. locationLookup.Keys, .. transitionsLookup.Keys];

            HashSet<string> markedForRemoval = [];
            if (conf.Logic?.Exclude != null)
            {
                HashSet<string> exclude = await conf.Logic.Exclude.GetContent();
                markedForRemoval.UnionWith(exclude);
            }
            HashSet<string> seen = [];
            Queue<string> toInclude = [];
            if (conf.Logic?.Include != null)
            {
                HashSet<string> include = await conf.Logic.Include.GetContent();
                foreach (string i in include)
                {
                    toInclude.Enqueue(i);
                }
                // mark for exclusion everything we know about that wasn't directly asked for.
                // later steps may un-mark it if it's required in logic.
                foreach (string n in allNames)
                {
                    if (!include.Contains(n))
                    {
                        markedForRemoval.Add(n);
                    }
                }
            }

            while (toInclude.Count > 0)
            {
                string next = toInclude.Dequeue();
                markedForRemoval.Remove(next);
                seen.Add(next);
                string logicString;
                if (waypointLookup.TryGetValue(next, out RawWaypointDef w))
                {
                    logicString = w.logic;
                }
                else if (locationLookup.TryGetValue(next, out RawLogicDef l))
                {
                    logicString = l.logic;
                }
                else if (transitionsLookup.TryGetValue(next, out RawLogicDef t))
                {
                    logicString = t.logic;
                }
                else
                {
                    continue;
                }
                Expr expr = LogicExpressionUtil.Parse(logicString);
                // enqueue only known location/waypoints from this logic block
                //   * anything that isn't a location/waypoint doesn't need to be inspected because it's not a removal candidate
                //   * anything that isn't part of this logic block isn't a removal candidate
                foreach (string term in ExtractTermAtoms(expr))
                {
                    if (allNames.Contains(term) && !seen.Contains(term))
                    {
                        toInclude.Enqueue(term);
                    }
                }
            }

            JsonLogicConfiguration filteredConfig = new()
            {
                Data = conf.Data,
                Logic = new()
                {
                    Terms = conf.Logic!.Terms,
                    Items = conf.Logic!.Items,
                    State = conf.Logic!.State,
                    Macros = conf.Logic!.Macros,
                    Waypoints = new([.. waypoints.Where(w => !markedForRemoval.Contains(w.name))]),
                    Locations = new([.. locations.Where(l => !markedForRemoval.Contains(l.name))]),
                    Transitions = new([.. transitions.Where(t => !markedForRemoval.Contains(t.name))]),
                }
            };
            finalConfigs.Add(filteredConfig);
        }
        return finalConfigs;
    }

    public static async Task<JsonLogicConfiguration> MergeManyAsync(IEnumerable<JsonLogicConfiguration> configs)
    {
        JsonLogicConfiguration merged = new();
        JsonData? mergedData = null;
        JsonLogic? mergedLogic = null;

        foreach (JsonLogicConfiguration config in configs)
        {
            // Merge Data
            if (config.Data != null)
            {
                if (mergedData == null)
                {
                    mergedData = new JsonData();
                }

                JsonData src = config.Data;
                mergedData.Rooms = await MergeMaybeFileDictAsync(mergedData.Rooms, src.Rooms);
                mergedData.Items = await MergeMaybeFileDictAsync(mergedData.Items, src.Items);
                mergedData.Locations = await MergeMaybeFileDictAsync(mergedData.Locations, src.Locations);
                mergedData.Transitions = await MergeMaybeFileDictAsync(mergedData.Transitions, src.Transitions);
                mergedData.Pools = await MergeMaybeFileListAsync(mergedData.Pools, src.Pools);
                mergedData.Costs = await MergeMaybeFileDictAsync(mergedData.Costs, src.Costs);
                mergedData.LogicSettings = await MergeMaybeFileDictAsync(mergedData.LogicSettings, src.LogicSettings);
                mergedData.Starts = await MergeMaybeFileDictAsync(mergedData.Starts, src.Starts);
            }
            // Merge Logic
            if (config.Logic != null)
            {
                if (mergedLogic == null)
                {
                    mergedLogic = new JsonLogic();
                }

                JsonLogic src = config.Logic;
                mergedLogic.Terms = await MergeMaybeFileDictListAsync(mergedLogic.Terms, src.Terms);
                // TODO - address this!
                if (src.State != null && mergedLogic.State != null)
                {
                    throw new InvalidOperationException("Merging state definitions is not currently supported");
                }
                mergedLogic.State = src.State ?? mergedLogic.State;
                mergedLogic.Macros = await MergeMaybeFileDictAsync(mergedLogic.Macros, src.Macros);
                mergedLogic.Transitions = await MergeMaybeFileListAsync(mergedLogic.Transitions, src.Transitions);
                mergedLogic.Locations = await MergeMaybeFileListAsync(mergedLogic.Locations, src.Locations);
                mergedLogic.Waypoints = await MergeMaybeFileListAsync(mergedLogic.Waypoints, src.Waypoints);
                mergedLogic.Items = await MergeMaybeFileListAsync(mergedLogic.Items, src.Items);
            }
        }
        merged.Data = mergedData;
        merged.Logic = mergedLogic;
        return merged;
    }

    // Helper for merging MaybeFile<Dictionary<string, T>>
    private static async Task<MaybeFile<Dictionary<string, T>>?> MergeMaybeFileDictAsync<T>(MaybeFile<Dictionary<string, T>>? a, MaybeFile<Dictionary<string, T>>? b)
    {
        if (a == null)
        {
            return b;
        }

        if (b == null)
        {
            return a;
        }

        Dictionary<string, T> dict = [];
        if (a != null)
        {
            Dictionary<string, T> d1 = await a.GetContent<Dictionary<string, T>>();
            if (d1 != null)
            {
                foreach (KeyValuePair<string, T> kv in d1)
                {
                    dict[kv.Key] = kv.Value;
                }
            }
        }
        if (b != null)
        {
            Dictionary<string, T> d2 = await b.GetContent<Dictionary<string, T>>();
            if (d2 != null)
            {
                foreach (KeyValuePair<string, T> kv in d2)
                {
                    dict[kv.Key] = kv.Value;
                }
            }
        }
        MaybeFile<Dictionary<string, T>> merged = new(dict);
        return merged;
    }

    // Helper for merging MaybeFile<List<T>>
    private static async Task<MaybeFile<List<T>>?> MergeMaybeFileListAsync<T>(MaybeFile<List<T>>? a, MaybeFile<List<T>>? b)
    {
        if (a == null)
        {
            return b;
        }

        if (b == null)
        {
            return a;
        }

        List<T> list = [];
        if (a != null)
        {
            List<T> l1 = await a.GetContent<List<T>>();
            if (l1 != null)
            {
                list.AddRange(l1);
            }
        }
        if (b != null)
        {
            List<T> l2 = await b.GetContent<List<T>>();
            if (l2 != null)
            {
                list.AddRange(l2);
            }
        }
        MaybeFile<List<T>> merged = new(list);
        return merged;
    }

    // Helper for merging MaybeFile<Dictionary<string, List<string>>>
    private static async Task<MaybeFile<Dictionary<string, List<string>>>?> MergeMaybeFileDictListAsync(MaybeFile<Dictionary<string, List<string>>>? a, MaybeFile<Dictionary<string, List<string>>>? b)
    {
        if (a == null)
        {
            return b;
        }

        if (b == null)
        {
            return a;
        }

        Dictionary<string, List<string>> dict = [];
        if (a != null)
        {
            Dictionary<string, List<string>> d1 = await a.GetContent<Dictionary<string, List<string>>>();
            if (d1 != null)
            {
                foreach (KeyValuePair<string, List<string>> kv in d1)
                {
                    dict[kv.Key] = new List<string>(kv.Value);
                }
            }
        }
        if (b != null)
        {
            Dictionary<string, List<string>> d2 = await b.GetContent<Dictionary<string, List<string>>>();
            if (d2 != null)
            {
                foreach (KeyValuePair<string, List<string>> kv in d2)
                {
                    if (dict.TryGetValue(kv.Key, out List<string>? existingList))
                    {
                        existingList.AddRange(kv.Value);
                    }
                    else
                    {
                        dict[kv.Key] = [.. kv.Value];
                    }
                }
            }
        }
        MaybeFile<Dictionary<string, List<string>>> merged = new(dict);
        return merged;
    }

    private static List<string> ExtractTermAtoms(Expr expr)
    {
        return expr switch
        {
            GroupingExpression<LogicExpressionType> g => ExtractTermAtoms(g.Nested),
            PrefixExpression<LogicExpressionType> p => ExtractTermAtoms(p.Operand),
            PostfixExpression<LogicExpressionType> p => ExtractTermAtoms(p.Operand),
            InfixExpression<LogicExpressionType> p => [.. ExtractTermAtoms(p.Left), .. ExtractTermAtoms(p.Right)],
            LogicAtomExpression s => [s.Print()],
            // numeric/bool/null atoms deliberately ignored
            _ => []
        };
    }
}

internal record JsonData
{
    public MaybeFile<Dictionary<string, RoomDef>>? Rooms { get; set; }
    public MaybeFile<Dictionary<string, ItemDef>>? Items { get; set; }
    public MaybeFile<Dictionary<string, LocationDef>>? Locations { get; set; }
    public MaybeFile<Dictionary<string, TransitionDef>>? Transitions { get; set; }
    public MaybeFile<List<PoolDef>>? Pools { get; set; }
    public MaybeFile<Dictionary<string, CostDef>>? Costs { get; set; }
    public MaybeFile<Dictionary<string, string>>? LogicSettings { get; set; }
    public MaybeFile<Dictionary<string, StartDef>>? Starts { get; set; }
}

internal record JsonLogic
{
    public MaybeFile<Dictionary<string, List<string>>>? Terms { get; set; }
    public MaybeFile<RawStateData>? State { get; set; }
    public MaybeFile<Dictionary<string, string>>? Macros { get; set; }
    public MaybeFile<List<RawLogicDef>>? Transitions { get; set; }
    public MaybeFile<List<RawLogicDef>>? Locations { get; set; }
    public MaybeFile<List<RawWaypointDef>>? Waypoints { get; set; }
    public MaybeFile<List<StringItemTemplate>>? Items { get; set; }

    /// <summary>
    /// Locations and waypoints to be retained. Items not here are implicitly excluded. If an item is in both Includes
    /// and Excludes, Includes takes precedence
    /// </summary>
    public MaybeFile<HashSet<string>>? Include { get; set; }
    /// <summary>
    /// Locations and waypoints to be removed. Items here may be retained if needed directly or indirectly by an included
    /// item.
    /// </summary>
    public MaybeFile<HashSet<string>>? Exclude { get; set; }

}
