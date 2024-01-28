using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace APHKLogicExtractor.DataModel
{
    [JsonConverter(typeof(StringEnumConverter))]
    internal enum ComparisonType
    {
        NoArgEquals,
        NoArgStartsWith,
        NoArgEndsWith,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    internal enum StateModifierKind
    {
        None,
        Beneficial,
        Detrimental,
        Mixed
    }

    internal record ArgumentClassifier(string Prefix, ComparisonType Comparison, string Test, StateModifierKind ClassificationWhenMatched)
    {
        public bool Matches(string prefix, string[] args)
        {
            if (prefix != Prefix)
            {
                return false;
            }
            switch (Comparison)
            {
                case ComparisonType.NoArgEquals:
                    return !args.Any(x => x == Test);
                case ComparisonType.NoArgStartsWith:
                    return !args.Any(x => x.StartsWith(Test));
                case ComparisonType.NoArgEndsWith:
                    return !args.Any(x => x.EndsWith(Test));
                default:
                    throw new ArgumentException("Invalid classification type");
            }
        }
    }

    internal record StateClassificationModel(HashSet<string> BeneficialModifiers,
        HashSet<string> DetrimentalModifiers, 
        HashSet<string>? OtherModifiers = null,
        List<ArgumentClassifier>? ArgumentClassifiers = null);
}
