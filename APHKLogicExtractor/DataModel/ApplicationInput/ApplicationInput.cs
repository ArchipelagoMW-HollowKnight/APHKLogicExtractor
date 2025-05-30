using APHKLogicExtractor.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace APHKLogicExtractor.DataModel;

[JsonConverter(typeof(StringEnumConverter))]
public enum InputType
{
    JsonLogic,
    RandoContext,
    WorldDefinition,
}

internal record ApplicationInput
{
    ///// Region extractor properties /////

    [JsonProperty(Required = Required.Always)]
    public InputType Type { get; set; }
    // JsonLogic => JsonLogicConfiguration
    // RandoContext => JToken
    // WorldDefinition => StringWorldDefinition
    [JsonProperty(Required = Required.Always)]
    public required MaybeFile<JToken> Configuration { get; set; }
    public string? StartStateTerm { get; set; }
    public MaybeFile<StateClassificationModel>? ClassifierModel { get; set; }
    public MaybeFile<HashSet<string>>? EmptyRegionsToKeep { get; set; }

    ///// Item extractor properties /////

    public MaybeFile<HashSet<string>>? IgnoredTerms { get; set; }
    public MaybeFile<HashSet<string>>? IgnoredItems { get; set; }
}
