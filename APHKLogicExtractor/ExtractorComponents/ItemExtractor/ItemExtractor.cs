using APHKLogicExtractor.DataModel;
using APHKLogicExtractor.DataModel.ItemExtractor;
using APHKLogicExtractor.RC;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RandomizerCore;
using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
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

            logger.LogInformation("Beginning item extraction");

            logger.LogInformation("Fetching logic");
            JsonLogicConfiguration configuration = await input.Configuration.GetContent<JsonLogicConfiguration>();
            Dictionary<string, List<string>> rawTerms = [];
            if (configuration.Logic?.Terms != null)
            {
                rawTerms = await configuration.Logic.Terms.GetContent();
            }

            TermCollectionBuilder terms = RcUtils.AssembleTerms(rawTerms);
            RawStateData stateData = new();
            if (configuration?.Logic?.State != null)
            {
                stateData = await configuration.Logic.State.GetContent();
            }

            List<RawLogicDef> transitionLogic = [];
            if (configuration?.Logic?.Transitions != null)
            {
                transitionLogic = await configuration.Logic.Transitions.GetContent();
            }

            List<RawLogicDef> locationLogic = [];
            if (configuration?.Logic?.Locations != null)
            {
                locationLogic = await configuration.Logic.Locations.GetContent();
            }

            Dictionary<string, string> macroLogic = [];
            if (configuration?.Logic?.Macros != null)
            {
                macroLogic = await configuration.Logic.Macros.GetContent();
            }

            List<RawWaypointDef> waypointLogic = [];
            if (configuration?.Logic?.Waypoints != null)
            {
                waypointLogic = await configuration.Logic.Waypoints.GetContent();
            }

            List<StringItemTemplate> itemTemplates = [];
            if (configuration?.Logic?.Items != null)
            {
                itemTemplates = await configuration.Logic.Items.GetContent();
            }

            logger.LogInformation("Preparing logic manager");
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
            foreach (RawLogicDef location in locationLogic)
            {
                preprocessorLmb.AddLogicDef(location);
            }
            foreach (StringItemTemplate template in itemTemplates)
            {
                preprocessorLmb.AddItem(template);
            }
            LogicManager lm = new(preprocessorLmb);

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
            foreach (var (name, effect) in progEffects)
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
            using (StreamWriter writer = outputManager.CreateOuputFileText("item_effects.py"))
            {
                pythonizer.Write(data, writer);
            }
            using (StreamWriter writer = outputManager.CreateOuputFileText("constants/item_names.py"))
            {
                pythonizer.WriteEnum("LocationNames",
                    progEffects.Keys.Concat(nonProgItems),
                    writer);
            }
            using (StreamWriter writer = outputManager.CreateOuputFileText("constants/terms.py"))
            {
                pythonizer.WriteEnum("Terms", terms.Select(t => t.Name)
                    .Concat(transitionLogic.Select(t => t.name))
                    .Concat(waypointLogic.Where(w => w.stateless).Select(w => w.name)), writer);
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
                    var (itemReqs, locationReqs, regionReqs) = clause.PartitionRequirements(lm);
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
