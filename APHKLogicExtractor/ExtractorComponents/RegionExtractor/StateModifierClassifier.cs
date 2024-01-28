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

    internal class StateModifierClassifier(VariableParser prefixParser)
    {
        // todo - consume these from input
        private static readonly HashSet<string> BeneficialStateModifiers = [
            "$BENCHRESET", // derived from named state, could be mixed but default beneficially resets most fields
            "$FLOWERGET",
            "$HOTSPRINGRESET", // derived from named state, could be mixed but default fills soul
            "$REGAINSOUL",
            "$STARTRESPAWN", // derived from named state, could be mixed but default recovers soul and HP from start location
        ];
        private static readonly HashSet<string> DetrimentalStateModifiers = [
            "$SHADESKIP",
            "$SPENDSOUL",
            "$TAKEDAMAGE",
            "$EQUIPCHARM",
            "$STAGSTATEMODIFIER",
            "$SAVEQUITRESET", // derived from named state, could be mixed but default just removes soul on save/quit or warp
        ];
        private static readonly HashSet<string> OtherStateModifiers = [
            "$CASTSPELL",
            "$SHRIEKPOGO",
            "$SLOPEBALL",
            "$WARPTOBENCH", // derived from named state (savequitreset + benchreset)
            "$WARPTOSTART", // derived from named state (savequitreset + startrespawn)
        ];

        public StateModifierKind ClassifySingle(TermToken token)
        {
            if (token is ComparisonToken)
            {
                // comparisons to state fields can never be beneficial, but may not always be detrimental
                return StateModifierKind.Mixed;
            }
            if (token is not SimpleToken st)
            {
                return StateModifierKind.None;
            }

            (string prefix, string[] args) = prefixParser.Parse(st.Name);
            if (prefix == "$CASTSPELL" || prefix == "$SLOPEBALL" || prefix == "$SHRIEKPOGO")
            {
                // without any soul gain, we can actually classify these as definitely detrimental
                if (args.Any(x => x.StartsWith("before:") || x.StartsWith("after:")))
                {
                    return StateModifierKind.Detrimental;
                }
                return StateModifierKind.Mixed;
            }

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
