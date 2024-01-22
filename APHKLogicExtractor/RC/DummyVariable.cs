using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;

namespace APHKLogicExtractor.RC
{
    internal class DummyVariable : StateModifier, IStateProvider
    {
        public override string Name { get; }

        public DummyVariable(string name)
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

        public StateUnion? GetInputState(object? sender, ProgressionManager pm)
        {
            throw new NotImplementedException();
        }
    }
}
