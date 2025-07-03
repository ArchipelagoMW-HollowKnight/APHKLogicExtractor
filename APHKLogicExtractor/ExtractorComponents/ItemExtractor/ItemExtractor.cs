using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.DataModel.ItemExtractor;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RandomizerCore;
using RandomizerCore.Logic;
using RandomizerCore.StringItems;
using System.Reflection;

namespace APHKLogicExtractor.ExtractorComponents.ItemExtractor
{
    internal class ItemExtractor(
        ApplicationInput input,
        ILogger<ItemExtractor> logger,
        IOptions<CommandLineOptions> optionsService,
        Pythonizer pythonizer,
        OutputManager outputManager) : BackgroundService
    {
        private static readonly FieldInfo AllOfEffect_Effects =
            typeof(AllOfEffect).GetField("Effects", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo FirstOfEffect_Effects =
            typeof(FirstOfEffect).GetField("Effects", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private CommandLineOptions options = optionsService.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Validating options");

            if (options.Jobs.Any() && !options.Jobs.Contains(JobType.ExtractItems))
            {
                logger.LogInformation("Job not requested, skipping");
                return;
            }

            if (input.Type != InputType.JsonLogic)
            {
                logger.LogWarning("Item extractor is not supported for non-JSON input types");
                return;
            }

            logger.LogInformation("Beginning item extraction");
            logger.LogInformation("Fetching logic");
            List<JsonLogicConfiguration> configs = await JsonLogicConfiguration.ParseManyAsync(input.Configuration);
            JsonLogicConfiguration configuration = await JsonLogicConfiguration.MergeManyAsync(configs);

            logger.LogInformation("Preparing logic manager");
            LogicManagerContext ctx = await RcUtils.ConstructLogicManager(configuration);
            LogicManager lm = ctx.LogicManager;

            logger.LogInformation("Processing item effects");
            HashSet<string> ignoredTerms = [];
            if (input.IgnoredTerms != null)
            {
                ignoredTerms = await input.IgnoredTerms.GetContent();
            }
            HashSet<string> ignoredItems = [];
            if (input.IgnoredItems != null)
            {
                ignoredItems = await input.IgnoredItems.GetContent();
            }
            Dictionary<string, IItemEffect> progEffects = new();
            List<string> nonProgItems = [];
            foreach (LogicItem li in lm.ItemLookup.Values)
            {
                if (ignoredItems.Contains(li.Name))
                {
                    continue;
                }

                if (li is not StringItem si)
                {
                    throw new NotImplementedException("Unrecognized item type");
                }
                IItemEffect? effect = ConvertAndSimplifyEffect(lm, si.Effect, ignoredTerms);
                if (effect == null)
                {
                    nonProgItems.Add(li.Name);
                }
                else
                {
                    progEffects[li.Name] = effect;
                }
            }

            logger.LogInformation("Collecting affected term maps");
            Dictionary<string, IReadOnlySet<string>> termsByItem = new();
            Dictionary<string, IReadOnlySet<string>> itemsByTerm = new();
            foreach ((string name, IItemEffect effect) in progEffects)
            {
                IReadOnlySet<string> affectedTerms = effect.GetAffectedTerms();
                termsByItem[name] = affectedTerms;
                foreach (string term in affectedTerms)
                {
                    if (!itemsByTerm.ContainsKey(term))
                    {
                        itemsByTerm[term] = new HashSet<string>();
                    }
                    HashSet<string> writable = (HashSet<string>)itemsByTerm[term];
                    writable.Add(name);
                }
            }

            logger.LogInformation("Beginning final output");
            ItemEffectData data = new(progEffects, nonProgItems, termsByItem, itemsByTerm);
            using (StreamWriter writer = outputManager.CreateOutputFileText("item_effects.py"))
            {
                pythonizer.Write(data, writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("constants/item_names.py"))
            {
                pythonizer.WriteEnum("ItemNames",
                    progEffects.Keys.Concat(nonProgItems),
                    writer);
            }
            using (StreamWriter writer = outputManager.CreateOutputFileText("constants/terms.py"))
            {
                pythonizer.WriteEnum("Terms", ctx.Terms.Select(t => t.Name)
                    .Concat(ctx.TransitionLogic.Select(t => t.name))
                    .Concat(ctx.WaypointLogic.Where(w => w.stateless).Select(w => w.name)), writer);
            }
            logger.LogInformation("Successfully exported {} progression items and {} non-progression items",
                progEffects.Count,
                nonProgItems.Count);
        }

        private IItemEffect? ConvertAndSimplifyEffect(LogicManager lm, StringItemEffect effect, HashSet<string> ignoredTerms)
        {
            if (effect is ConditionalEffect ce && ce.Logic is DNFLogicDef dd)
            {
                List<StatefulClause> clauses = RcUtils.GetDnfClauses(lm, dd);
                List<RequirementBranch> branches = [];
                foreach (StatefulClause clause in clauses)
                {
                    if (clause.StateProvider != null || clause.StateModifiers.Count > 0)
                    {
                        throw new NotImplementedException("Stateful item effects are not yet supported");
                    }
                    (HashSet<string> itemReqs, HashSet<string> locationReqs, HashSet<string> regionReqs)
                        = clause.PartitionRequirements(lm);
                    branches.Add(new RequirementBranch(itemReqs, locationReqs, regionReqs, []));
                }
                IItemEffect? simplified = ConvertAndSimplifyEffect(lm, ce.Effect, ignoredTerms);
                if (simplified == null)
                {
                    return null;
                }
                return new ConditionedEffect(branches, ce.Negated, simplified);
            }
            // MaxWithEffect intentionally not supported, as it is probably unsafe. Can be revisited in the future.
            IItemEffect? result = effect switch
            {
                EmptyEffect => null,
                AllOfEffect ae => new MultiEffect(((StringItemEffect[])AllOfEffect_Effects.GetValue(ae)!)
                    .Select(e => ConvertAndSimplifyEffect(lm, e, ignoredTerms)!)
                    .Where(e => e != null)
                    .ToList()),
                FirstOfEffect fe => new BranchingEffect(((StringItemEffect[])FirstOfEffect_Effects.GetValue(fe)!)
                    .Select(e => ConvertAndSimplifyEffect(lm, e, ignoredTerms)!)
                    .Where(e => e != null)
                    .ToList()),
                IncrementEffect ie when ignoredTerms.Contains(ie.Term.Name) => null,
                IncrementEffect ie => new IncrementTermsEffect(new Dictionary<string, int>()
                {
                    [ie.Term.Name] = ie.Value,
                }),
                ReferenceEffect re when re.Item is StringItem ri => ConvertAndSimplifyEffect(lm, ri.Effect, ignoredTerms),
                _ => throw new NotImplementedException("Unrecognized effect")
            };
            return result?.Simplify();
        }
    }
}
