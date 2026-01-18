using RandomizerCore.Logic;
using RandomizerCore.Logic.StateLogic;
using System.Diagnostics.CodeAnalysis;

namespace APHKLogicExtractor.RC
{
    internal class DummyVariableResolver(RawStateData rawStateData) : VariableResolver
    {
        public override StateManagerBuilder GetStateModel()
        {
            StateManagerBuilder smb = base.GetStateModel();
            smb.AppendRawStateData(rawStateData);
            return smb;
        }

        public override bool TryMatch(LogicManager lm, string term, [MaybeNullWhen(false)] out LogicVariable variable)
        {
            if (base.TryMatch(lm, term, out variable))
            {
                return true;
            }

            if (term.StartsWith('$'))
            {
                // todo: remove this hk-specific assumption
                if (term.StartsWith("$StartLocation"))
                {
                    variable = new DummyStateProvider(term);
                    return true;
                }
                else
                {
                    variable = new DummyStateModifier(term);
                    return true;
                }
            }
            return false;
        }
    }
}
