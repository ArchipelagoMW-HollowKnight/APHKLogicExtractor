using RandomizerCore.Logic;
using System.Diagnostics.CodeAnalysis;

namespace APHKLogicExtractor.RC
{
    internal class DummyVariableResolver : VariableResolver
    {
        public override bool TryMatch(LogicManager lm, string term, [MaybeNullWhen(false)] out LogicVariable variable)
        {
            if (base.TryMatch(lm, term, out variable))
            {
                return true;
            }

            if (term.StartsWith('$'))
            {
                variable = new DummyVariable(term);
                return true;
            }
            return false;
        }
    }
}
