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

internal record ApplicationInput
{
    public InputType Type { get; set; }
    // JsonLogic => JsonLogicConfiguration
    // RandoContext => JToken
    // WorldDefinition => StringWorldDefinition
    public MaybeFile<JToken> Configuration { get; set; }
    public string? StartStateTerm { get; set; }
    public MaybeFile<StateClassificationModel>? ClassifierModel { get; set; }
    public MaybeFile<HashSet<string>>? EmptyRegionsToKeep { get; set; }
}
