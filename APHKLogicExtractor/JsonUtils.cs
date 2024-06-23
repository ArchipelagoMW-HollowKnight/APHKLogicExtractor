using APHKLogicExtractor.Loader;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RandomizerCore.Json.Converters;

namespace APHKLogicExtractor;

public static class JsonUtils
{
    public static JsonSerializer GetSerializer(ResourceLoader resourceLoader)
    {
        return new JsonSerializer
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
            ContractResolver = new RCContractResolver(),
            SerializationBinder = new RCSerializationBinder(),
            Converters =
            {
                new StringEnumConverter(),
                new MaybeFileConverter(resourceLoader),
                new LogicProcessorConverter(),
                new LMConverter(),
                new RandoContextConverter()
            }
        };
    }
}
