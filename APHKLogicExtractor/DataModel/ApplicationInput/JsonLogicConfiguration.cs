using APHKLogicExtractor.DataModel.RandomizerData;
using APHKLogicExtractor.Loader;
using Newtonsoft.Json.Linq;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringItems;

namespace APHKLogicExtractor.DataModel;

internal record JsonLogicConfiguration
{
    public JsonData? Data { get; set; }
    public JsonLogic? Logic { get; set; }

    public static async Task<List<JsonLogicConfiguration>> ParseManyAsync(MaybeFile<JToken> configFile)
    {
        return await configFile.GetContent<List<JsonLogicConfiguration>>();
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
                if (src.State == null && mergedLogic.State != null)
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
}
