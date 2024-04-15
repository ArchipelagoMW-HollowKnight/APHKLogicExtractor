using APHKLogicExtractor.Loaders;
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
        ILogger<ItemExtractor> logger,
        IOptions<CommandLineOptions> optionsService,
        LogicLoader logicLoader,
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
            TermCollectionBuilder terms = await logicLoader.LoadTerms();
            RawStateData stateData = await logicLoader.LoadStateFields();
            List<RawLogicDef> transitionLogic = await logicLoader.LoadTransitions();
            List<RawLogicDef> locationLogic = await logicLoader.LoadLocations();
            Dictionary<string, string> macroLogic = await logicLoader.LoadMacros();
            List<RawWaypointDef> waypointLogic = await logicLoader.LoadWaypoints();
            List<StringItemTemplate> itemTemplates = await logicLoader.LoadItems();

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

            foreach (LogicItem li in lm.ItemLookup.Values)
            {
                if (li is not StringItem si)
                {
                    throw new NotImplementedException("Unrecognized item type");
                }
                logger.LogInformation("{}: {} (from {})", si.Name, StringifyEffect(si.Effect), si.EffectString);
            }
        }

        private string StringifyEffect(StringItemEffect effect)
        {
            return effect switch
            {
                EmptyEffect => "No effect",
                AllOfEffect ae => string.Join(" AND ", ((StringItemEffect[])AllOfEffect_Effects.GetValue(ae)!).Select(StringifyEffect)),
                FirstOfEffect fe => string.Join(" ELSE ", ((StringItemEffect[])FirstOfEffect_Effects.GetValue(fe)!).Select(StringifyEffect)),
                IncrementEffect ie => $"add {ie.Value} to {ie.Term.Name}",
                MaxWithEffect me => $"set {me.Term.Name} no smaller than {me.Value}",
                ConditionalEffect ce => $"if `{ce.Logic.InfixSource}` is {(ce.Negated ? "true" : "false")}, then {{{StringifyEffect(ce.Effect)}}}",
                ReferenceEffect re when re.Item is StringItem ri => StringifyEffect(ri.Effect),
                _ => throw new NotImplementedException("Unrecognized effect")
            }; 
        }
    }
}
