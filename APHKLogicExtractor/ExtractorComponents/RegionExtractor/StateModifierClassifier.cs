using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal enum StateModifierKind
    {
        None,
        Beneficial,
        Detrimental,
        Mixed
    }

    internal class StateModifierClassifier(TermPrefixParser prefixParser)
    {
        // todo - consume these from input
        // todo - benchreset and hotspringreset should be default mixed as they are derived from named states
        private static readonly HashSet<string> BeneficialStateModifiers = [
            "$BENCHRESET",
            "$FLOWERGET",
            "$HOTSPRINGRESET",
            "$REGAINSOUL"
        ];
        private static readonly HashSet<string> DetrimentalStateModifiers = [
            "$SHADESKIP",
            "$SPENDSOUL",
            "$TAKEDAMAGE",
            "$EQUIPCHARM",
            "$STAGSTATEMODIFIER"
        ];
        private static readonly HashSet<string> OtherStateModifiers = [
            "$CASTSPELL",
            "$SHRIEKPOGO",
            "$SLOPEBALL",
            "$SAVEQUITRESET",
            "$STARTRESPAWN",
            "$WARPTOBENCH",
            "$WARPTOSTART"
        ];

        public StateModifierKind ClassifySingle(TermToken token)
        {
            if (token is not SimpleToken st)
            {
                return StateModifierKind.None;
            }

            string prefix = prefixParser.GetPrefix(st.Name);
            if (BeneficialStateModifiers.Contains(prefix))
            {
                return StateModifierKind.Beneficial;
            }
            if (DetrimentalStateModifiers.Contains(prefix))
            {
                return StateModifierKind.Detrimental;
            }
            if (OtherStateModifiers.Contains(prefix))
            {
                return StateModifierKind.Mixed;
            }

            return StateModifierKind.None;
        }

        public StateModifierKind ClassifyMany(IEnumerable<TermToken> tokens)
        {
            StateModifierKind aggregated = StateModifierKind.None;
            foreach (TermToken token in tokens)
            {
                StateModifierKind kind = ClassifySingle(token);
                if (kind == StateModifierKind.Beneficial && aggregated == StateModifierKind.Detrimental)
                {
                    return StateModifierKind.Mixed;
                }
                if (kind == StateModifierKind.Detrimental && aggregated == StateModifierKind.Beneficial)
                {
                    return StateModifierKind.Mixed;
                }
                if (kind == StateModifierKind.Mixed)
                {
                    return StateModifierKind.Mixed;
                }

                // either the same, or improving None to the current kind
                aggregated = kind;
            }
            return aggregated;
        }
    }
}
