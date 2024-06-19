using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RandomizerCore.Json;

namespace APHKLogicExtractor.Loader;

public class MaybeFileConverter(ResourceLoader resourceLoader) : JsonConverter
{
    static readonly Type target = typeof(MaybeFile<>).GetGenericTypeDefinition();

    public override bool CanConvert(Type objectType)
    {
        return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == MaybeFileConverter.target;
    }

    public override object? ReadJson(
        JsonReader reader,
        Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        // The token is a string, therefore we'll try to load using the resourceLoader.
        if (reader.TokenType == JsonToken.String)
        {
            var instance = Activator.CreateInstance(objectType);
            objectType.GetProperty("Lazy")?.SetValue(instance, (resourceLoader, reader.Value as string));
            return instance;
        }

        // We detected an object or array in the JSON, therefore, we'll try to parse as usual.
        if (reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.StartArray)
        {
            // Grabs `MaybeFile` generic argument `T`
            var innerType = objectType.GetGenericArguments()[0];

            var deserialized = serializer.Deserialize(reader, innerType);
            var instance = Activator.CreateInstance(objectType);
            objectType.GetProperty(nameof(MaybeFile<object>.Content))?.SetValue(instance, deserialized);
            if (innerType == typeof(JToken))
                objectType.GetProperty(nameof(MaybeFile<object>.Serializer))?.SetValue(instance, serializer);
            return instance;
        }

        throw new Exception($"Unable to deserialize token {reader.TokenType}.");
    }

    public override void WriteJson(
        JsonWriter writer,
        object? value,
        JsonSerializer serializer)
    {
        var objType = value?.GetType();
        if (objType == null || objType.GetGenericTypeDefinition() != target)
        {
            throw new Exception($"Was expecting a MaybeFile");
        }

        var lazy = objType.GetProperty(nameof(MaybeFile<object>.Lazy))?.GetValue(value);
        if (lazy != null)
        {
            serializer.Serialize(writer, null);
            return;
        }

        var content = objType.GetProperty(nameof(MaybeFile<object>.Content))?.GetValue(value);
        serializer.Serialize(writer, content);
    }
}

public class MaybeFile<T> where T : class
{
    public (ResourceLoader loader, string uri)? Lazy { get; set; }
    public JsonSerializer? Serializer { get; set; }
    public object? Content { get; set; }

    public Task<T> GetContent()
    {
        return this.GetContent<T>();
    }

    public async Task<T2> GetContent<T2>() where T2 : class
    {
        if (this.Lazy != null)
        {
            var (loader, uri) = this.Lazy.Value;
            var rawContent = await loader.Load(uri);

            this.Content = rawContent.AsJson<T2>();
            this.Lazy = null;
        }

        if (this.Serializer != null && typeof(T2) != typeof(JToken) && this.Content is JToken tokens)
        {
            this.Content = tokens.ToObject<T2>(this.Serializer);
        }

        if (this.Content is T2 content)
            return content;

        throw new Exception($"Unable to cast content to the given type: {typeof(T2)} from {this.Content?.GetType().ToString() ?? "null"}");
    }
};
