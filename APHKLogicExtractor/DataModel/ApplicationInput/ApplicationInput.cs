using APHKLogicExtractor.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using RandomizerCore.Json.Converters;

namespace APHKLogicExtractor.DataModel;

[JsonConverter(typeof(StringEnumConverter))]
public enum InputType
{
    JsonLogic,
    RandoContext,
    WorldDefinition,
}

public record ApplicationInput
{
    [JsonProperty]
    internal InputType type;
    // JsonLogic => JsonLogicConfiguration
    // RandoContext => JToken
    // WorldDefinition => StringWorldDefinition
    [JsonProperty]
    internal MaybeFile<JToken> configuration;
    [JsonProperty]
    internal string? startStateTerm;
    [JsonProperty]
    internal MaybeFile<StateClassificationModel>? classifierModel;
    [JsonProperty]
    internal MaybeFile<HashSet<string>>? emptyRegionsToKeep;
}
