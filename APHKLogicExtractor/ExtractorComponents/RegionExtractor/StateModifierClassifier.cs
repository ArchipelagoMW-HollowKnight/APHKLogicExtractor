using APHKLogicExtractor.DataModel;
using Microsoft.Extensions.Options;
using RandomizerCore.Json;
using RandomizerCore.StringLogic;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal class StateModifierClassifier
    {
        private VariableParser prefixParser;
        private StateClassificationModel classificationModel;

        public StateModifierClassifier(ApplicationInput input, VariableParser prefixParser)
        {
            this.prefixParser = prefixParser;
            if (input.ClassifierModel != null)
            {
                classificationModel = input.ClassifierModel.GetContent().Result ?? new([], []);
            }
            else
            {
                classificationModel = new([], []);
            }
        }

        public StateModifierKind ClassifySingle(string token)
        {
            return ClassifySingle(Utils.ParseSingleToken(token));
        }

        public StateModifierKind ClassifyMany(IEnumerable<string> tokens)
        {
            return ClassifyMany(tokens.Select(Utils.ParseSingleToken));
        }

        public StateModifierKind ClassifySingle(TermToken token)
        {
            if (token is ComparisonToken)
            {
                // comparisons to state fields can never be beneficial, but may not always be detrimental
                return StateModifierKind.Mixed;
            }
            if (token is not SimpleToken st)
            {
                return StateModifierKind.Mixed;
            }

            (string prefix, string[] args) = prefixParser.Parse(st.Name);
            foreach (ArgumentClassifier argumentClassifier in classificationModel.ArgumentClassifiers ?? [])
            {
                if (argumentClassifier.Matches(prefix, args))
                {
                    return argumentClassifier.ClassificationWhenMatched;
                }
            }

            if (classificationModel.BeneficialModifiers.Contains(prefix))
            {
                return StateModifierKind.Beneficial;
            }
            if (classificationModel.DetrimentalModifiers.Contains(prefix))
            {
                return StateModifierKind.Detrimental;
            }
            return StateModifierKind.Mixed;
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
