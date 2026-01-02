using APHKLogicExtractor.DataModel;
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
                classificationModel = input.ClassifierModel.GetContent().Result;
            }
            else
            {
                classificationModel = new([], []);
            }
        }

        public StateModifierKind ClassifySingle(string token)
        {
            return ClassifySingle(LogicExpressionUtil.Parse(token));
        }

        public StateModifierKind ClassifyMany(IEnumerable<string> tokens)
        {
            return ClassifyMany(tokens.Select(LogicExpressionUtil.Parse));
        }

        public StateModifierKind ClassifySingle(Expr expr)
        {
            if (expr is ComparisonExpression)
            {
                // comparisons to state fields can never be beneficial, but may not always be detrimental
                return StateModifierKind.Mixed;
            }
            if (expr is not Atom a)
            {
                return StateModifierKind.Mixed;
            }

            (string prefix, string[] args) = prefixParser.Parse(a.Print());
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

        public StateModifierKind ClassifyMany(IEnumerable<Expr> exprs)
        {
            StateModifierKind aggregated = StateModifierKind.None;
            foreach (Expr expr in exprs)
            {
                StateModifierKind kind = ClassifySingle(expr);
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
