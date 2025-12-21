using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;

namespace APHKLogicExtractor.RC
{
    internal class DummyStateModifier : StateModifier
    {
        public override string Name { get; }

        public DummyStateModifier(string name)
        {
            Name = name;
        }

        public override IEnumerable<Term> GetTerms()
        {
            yield break;
        }

        public override IEnumerable<LazyStateBuilder> ModifyState(object? sender, ProgressionManager pm, LazyStateBuilder state)
        {
            yield return state;
        }
    }
}
