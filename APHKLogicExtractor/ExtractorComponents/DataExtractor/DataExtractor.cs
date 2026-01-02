using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.DataModel.DataExtractor;
using APHKLogicExtractor.DataModel.RandomizerData;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;

namespace APHKLogicExtractor.ExtractorComponents.DataExtractor
{
    internal class DataExtractor(
        ApplicationInput input,
        ILogger<DataExtractor> logger,
        IOptions<CommandLineOptions> optionsService,
        IdFactory idFactory,
        Pythonizer pythonizer,
        OutputManager outputManager) : BackgroundService
    {
        // these costs are not anywhere in randomizer data so we get to hardcode them
        private static readonly Dictionary<(string loc, string item), List<CostDef>> ShopGeoCosts = new()
        {
            [("Sly", "Simple_Key")] = [new("GEO", 950)],
            [("Sly", "Rancid_Egg")] = [new("GEO", 60)],
            [("Sly", "Lumafly_Lantern")] = [new("GEO", 1800)],
            [("Sly", "Gathering_Swarm")] = [new("GEO", 300)],
            [("Sly", "Stalwart_Shell")] = [new("GEO", 200)],
            [("Sly", "Mask_Shard")] = [
                new("GEO", 150),
                new("GEO", 500),
            ],
            [("Sly", "Vessel_Fragment")] = [new("GEO", 550)],

            [("Sly_(Key)", "Heavy_Blow")] = [new("GEO", 350)],
            [("Sly_(Key)", "Elegant_Key")] = [new("GEO", 800)],
            [("Sly_(Key)", "Mask_Shard")] = [
                new("GEO", 800),
                new("GEO", 1500),
            ],
            [("Sly_(Key)", "Vessel_Fragment")] = [new("GEO", 900)],
            [("Sly_(Key)", "Sprintmaster")] = [new("GEO", 400)],

            [("Iselda", "Wayward_Compass")] = [new("GEO", 220)],
            [("Iselda", "Quill")] = [new("GEO", 120)],

            [("Salubra", "Lifeblood_Heart")] = [new("GEO", 250)],
            [("Salubra", "Longnail")] = [new("GEO", 300)],
            [("Salubra", "Steady_Body")] = [new("GEO", 120)],
            [("Salubra", "Shaman_Stone")] = [new("GEO", 220)],
            [("Salubra", "Quick_Focus")] = [new("GEO", 800)],

            [("Leg_Eater", "Fragile_Heart")] = [new("GEO", 350)],
            [("Leg_Eater", "Fragile_Greed")] = [new("GEO", 250)],
            [("Leg_Eater", "Fragile_Strength")] = [new("GEO", 600)],
        };
        private static readonly Dictionary<int, int> SalubraGeoCostsByCharmCount = new()
        {
            [5] = 120,
            [10] = 500,
            [18] = 900,
            [25] = 1400,
            [40] = 800,
        };
        private static readonly string[] TrandoSettingsTerms =
        [
            "ITEMRANDO",
            "MAPAREARANDO",
            "FULLAREARANDO",
            "AREARANDO",
            "ROOMRANDO",
            "SWIM",
            "ELEVATOR",
            "2MASKS",
            "VERTICAL"
        ];

        private CommandLineOptions options = optionsService.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Validating options");

            if (options.Jobs.Any() && !options.Jobs.Contains(JobType.ExtractData))
            {
                logger.LogInformation("Job not requested, skipping");
                return;
            }

            if (input.Type != InputType.JsonLogic)
            {
                logger.LogWarning("Data extractor is not supported for non-JSON input types");
                return;
            }

            logger.LogInformation("Beginning data extraction");
            List<JsonLogicConfiguration> configs = await JsonLogicConfiguration.ParseManyAsync(input.Configuration);
            JsonLogicConfiguration configuration = await JsonLogicConfiguration.MergeManyAsync(configs);

            logger.LogInformation("Collecting scene metadata");
            Dictionary<string, RoomDef> sceneData = [];
            if (configuration.Data?.Rooms != null)
            {
                sceneData = await configuration.Data.Rooms.GetContent();
            }

            logger.LogInformation("Collecting pool and cost information");
            List<PoolDef> pools = [];
            Dictionary<string, CostDef> vanillaLocationCosts = [];
            Dictionary<string, string> logicOptions = [];
            if (configuration.Data?.Pools != null)
            {
                pools = await configuration.Data.Pools.GetContent();
            }
            if (configuration.Data?.Costs != null)
            {
                vanillaLocationCosts = await configuration.Data.Costs.GetContent();
            }
            if (configuration.Data?.LogicSettings != null)
            {
                logicOptions = await configuration.Data.LogicSettings.GetContent();
            }
            Dictionary<(string loc, string item), int> usedCostsByItemLocationPair = new();
            Dictionary<string, ApPoolDef> finalPoolOptions = new();
            string optionName = "";
            foreach (PoolDef pool in pools)
            {
                if (pool.Path.ToLowerInvariant() != "false")
                {
                    // inject vanilla costs
                    foreach (VanillaDef vanilla in pool.Vanilla)
                    {
                        List<CostDef> costsToAdd = [];
                        // ISCP geo costs
                        if (vanillaLocationCosts.TryGetValue(vanilla.Location, out CostDef? cd) && cd.Term == "GEO" && cd.Amount > 0)
                        {
                            costsToAdd.Add(cd);
                        }
                        // shop geo costs
                        (string Location, string Item) itemLocationPair = (vanilla.Location, vanilla.Item);
                        int i = usedCostsByItemLocationPair.GetValueOrDefault(itemLocationPair, 0);
                        if (ShopGeoCosts.TryGetValue(itemLocationPair, out List<CostDef>? costs)
                            && i < costs.Count)
                        {
                            costsToAdd.Add(costs[i++]);
                            usedCostsByItemLocationPair[itemLocationPair] = i;
                        }
                        // salubra charm notch geo costs
                        CostDef? charmCost = vanilla.Costs?.FirstOrDefault(c => c.Term == "CHARMS");
                        if (vanilla.Location == "Salubra" && charmCost != null)
                        {
                            costsToAdd.Add(new CostDef("GEO", SalubraGeoCostsByCharmCount[charmCost.Amount]));
                        }
                        vanilla.Costs ??= [];
                        vanilla.Costs.AddRange(costsToAdd);
                    }
                    // get the corresponding option name, or inherit the last one if not provided
                    if (pool.Path != "")
                    {
                        optionName = GetOptionName(pool.Path, "Randomize");
                    }

                    if (pool.Vanilla.Count > 0)
                    {
                        if (!finalPoolOptions.ContainsKey(optionName))
                        {
                            finalPoolOptions[optionName] = ApPoolDef.Empty();
                        }
                        finalPoolOptions[optionName].Randomized.Items.AddRange(pool.IncludeItems);
                        finalPoolOptions[optionName].Randomized.Locations.AddRange(pool.IncludeLocations);
                        finalPoolOptions[optionName].Vanilla.AddRange(pool.Vanilla);
                    }
                }
            }
            logicOptions = logicOptions.ToDictionary(kv => kv.Key, kv => GetOptionName(kv.Value, ""));

            logger.LogInformation("Collecting item data");
            Dictionary<string, int> itemNameToId = new();
            Dictionary<string, int> itemGeoCostCaps = new();
            HashSet<string> itemsToIgnore = [];
            if (input.IgnoredItems != null)
            {
                itemsToIgnore = await input.IgnoredItems.GetContent();
            }
            if (configuration.Data?.Items != null)
            {
                Dictionary<string, ItemDef> itemDefs = await configuration.Data.Items.GetContent();
                itemNameToId = await idFactory.CreateIds(0, itemDefs.Keys.Where(x => !itemsToIgnore.Contains(x)), []);
                itemGeoCostCaps = itemDefs.Values.Where(x => !itemsToIgnore.Contains(x.Name)).ToDictionary(x => x.Name, x => x.PriceCap);
            }

            logger.LogInformation("Collecting location data");
            Dictionary<string, LocationDetails> locations = [];
            List<string> multiLocations = [];
            Dictionary<string, int> locationNameToId = new();
            if (configuration.Data?.Locations != null)
            {
                Dictionary<string, LocationDef> locationDefs = await configuration.Data.Locations.GetContent();
                foreach ((string location, LocationDef def) in locationDefs)
                {
                    RoomDef? scene = sceneData.GetValueOrDefault(def.SceneName);
                    LocationDetails transformed = new(
                        scene?.MapArea ?? "",
                        scene?.TitledArea ?? ""
                    );
                    locations[location] = transformed;
                    if (def.FlexibleCount)
                    {
                        multiLocations.Add(location);
                    }
                }
                locationNameToId = await idFactory.CreateIds(0, locationDefs.Keys, multiLocations.ToDictionary(x => x, x => 16));
            }

            logger.LogInformation("Collecting trando and start data");
            Dictionary<string, TransitionDetails> transitions = [];
            Dictionary<string, StartDef> starts = [];
            Dictionary<string, StartDetails> finalStarts = [];
            if (configuration.Data?.Transitions != null)
            {
                Dictionary<string, TransitionDef> transitionDefs = await configuration.Data.Transitions.GetContent();
                foreach ((string transition, TransitionDef def) in transitionDefs)
                {
                    RoomDef? scene = sceneData.GetValueOrDefault(def.SceneName);
                    TransitionDetails transformed = new(
                        def.VanillaTarget,
                        def.Direction,
                        def.Sides,
                        scene?.MapArea ?? "",
                        def.IsMapAreaTransition,
                        scene?.TitledArea ?? "",
                        def.IsTitledAreaTransition
                    );
                    transitions[transition] = transformed;
                }
            }
            if (configuration.Data?.Starts != null)
            {
                starts = await configuration.Data.Starts.GetContent();
            }
            // transform start data and parse out logic
            LogicManager lm = GetSettingsLogicManager(logicOptions.Keys);
            foreach (StartDef start in starts.Values)
            {
                DNFLogicDef logic = lm.CreateDNFLogicDef(new RawLogicDef(start.Name, start.Logic));
                List<StatefulClause> clauses = RcUtils.GetDnfClauses(lm, logic);
                List<RequirementBranch> finalLogic = [];
                foreach (StatefulClause clause in clauses)
                {
                    // these can't be stateful so the conversion to branches is easy
                    (HashSet<string> itemReqs, HashSet<string> locationReqs, HashSet<string> regionReqs)
                        = clause.PartitionRequirements(lm);
                    if (itemReqs.Count > 0 || locationReqs.Count > 0 || regionReqs.Count > 0)
                    {
                        finalLogic.Add(new RequirementBranch(itemReqs, locationReqs, regionReqs, []));
                    }
                }
                finalStarts.Add(pythonizer.PythonizeName(start.Name), new StartDetails(start.Name, start.Transition, finalLogic));
            }

            logger.LogInformation("Collecting state data");
            Dictionary<string, int> stateFieldDefaults = new();
            if (configuration.Logic?.State != null)
            {
                RawStateData rawStateData = await configuration.Logic.State.GetContent();
                StateManagerBuilder smb = new();
                smb.AppendRawStateData(rawStateData);
                StateManager sm = new(smb);

                foreach (StateInt @int in sm.Ints)
                {
                    stateFieldDefaults[@int.Name] = @int.GetDefaultValue(sm);
                }
                foreach (StateBool @bool in sm.Bools)
                {
                    stateFieldDefaults[@bool.Name] = @bool.GetDefaultValue(sm) ? 1 : 0;
                }
            }

            logger.LogInformation("Beginning final output");
            IdData idData = new(itemNameToId, locationNameToId);
            PoolData poolData = new(finalPoolOptions, logicOptions);
            ItemData itemData = new(itemGeoCostCaps);
            LocationData locationData = new(locations, multiLocations);
            TrandoData trandoData = new(transitions, finalStarts);
            StateData stateData = new(stateFieldDefaults);
            using (StreamWriter writer = outputManager.CreateOutputFileText("ids.py"))
            {
                pythonizer.Write(idData, writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("json/itemNameToId.json"))
            {
                JsonUtils.GetSerializer().Serialize(writer, idData.itemNameToId);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("json/locationNameToId.json"))
            {
                JsonUtils.GetSerializer().Serialize(writer, idData.locationNameToId);
            }

            using (StreamWriter writer = outputManager.CreateOutputFileText("option_data.py"))
            {
                pythonizer.Write(poolData, writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("item_data.py"))
            {
                pythonizer.Write(itemData, writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("location_data.py"))
            {
                pythonizer.Write(locationData, writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("trando_data.py"))
            {
                pythonizer.Write(trandoData, writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("state_data.py"))
            {
                pythonizer.Write(stateData, writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("constants/map_area_names.py"))
            {
                pythonizer.WriteEnum("MapAreaNames", sceneData.Values.Select(s => s.MapArea).Distinct(), writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("constants/titled_area_names.py"))
            {
                pythonizer.WriteEnum("TitledAreaNames", sceneData.Values.Select(s => s.TitledArea).Distinct(), writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("constants/state_field_names.py"))
            {
                pythonizer.WriteEnum("StateFieldNames", stateData.FieldDefaults.Keys, writer);
            }

            logger.LogInformation("Successfully extracted non-logic data");
        }

        private string GetOptionName(string path, string prefix)
        {
            string basename = path.Split('.')[^1];
            if (!basename.StartsWith(prefix))
            {
                basename = prefix + basename;
            }
            return basename;
        }

        private LogicManager GetSettingsLogicManager(IEnumerable<string> logicOptionTerms)
        {
            logger.LogInformation("Preparing settings logic manager");

            LogicManagerBuilder lmb = new();
            foreach (string term in logicOptionTerms.Concat(TrandoSettingsTerms))
            {
                lmb.GetOrAddTerm(term, TermType.SignedByte);
            }

            return new LogicManager(lmb);
        }
    }
}
