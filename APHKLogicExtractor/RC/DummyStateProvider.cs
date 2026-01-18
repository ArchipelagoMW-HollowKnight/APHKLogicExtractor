using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;

namespace APHKLogicExtractor.RC;

internal class DummyStateProvider : StateProvider
{
    public override string Name { get; }

    public DummyStateProvider(string name)
    {
        Name = name;
    }

    public override StateUnion? GetInputState(object? sender, ProgressionManager pm)
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<Term> GetTerms()
    {
        yield break;
    }
}
