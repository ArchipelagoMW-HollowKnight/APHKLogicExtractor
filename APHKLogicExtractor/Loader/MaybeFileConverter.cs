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
            object? instance = Activator.CreateInstance(objectType);
            objectType.GetProperty("Lazy")?.SetValue(instance, (resourceLoader, reader.Value as string));
            return instance;
        }

        // We detected an object or array in the JSON, therefore, we'll try to parse as usual.
        if (reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.StartArray)
        {
            // Grabs `MaybeFile` generic argument `T`
            Type innerType = objectType.GetGenericArguments()[0];

            object? deserialized = serializer.Deserialize(reader, innerType);
            object? instance = Activator.CreateInstance(objectType);
            objectType.GetProperty(nameof(MaybeFile<object>.Content))?.SetValue(instance, deserialized);
            if (innerType == typeof(JToken))
                objectType.GetProperty(nameof(MaybeFile<object>.Serializer))?.SetValue(instance, serializer);
            return instance;
        }

        throw new JsonSerializationException($"Unable to deserialize token {reader.TokenType}.");
    }

    public override void WriteJson(
        JsonWriter writer,
        object? value,
        JsonSerializer serializer)
    {
        Type objType = value!.GetType();

        object? lazy = objType.GetProperty(nameof(MaybeFile<object>.Lazy))?.GetValue(value);
        if (lazy != null)
        {
            serializer.Serialize(writer, null);
            return;
        }

        object? content = objType.GetProperty(nameof(MaybeFile<object>.Content))?.GetValue(value);
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

    public async Task<U> GetContent<U>() where U : class
    {
        if (this.Lazy != null)
        {
            var (loader, uri) = this.Lazy.Value;
            ResourceLoader.Content rawContent = await loader.Load(uri);

            this.Content = rawContent.AsJson<U>();
            this.Lazy = null;
        }

        if (this.Serializer != null && typeof(U) != typeof(JToken) && this.Content is JToken tokens)
        {
            this.Content = tokens.ToObject<U>(this.Serializer);
        }

        if (this.Content is U content)
            return content;

        throw new InvalidCastException($"Unable to cast content to the given type: {typeof(U)} from {this.Content?.GetType().ToString() ?? "null"}");
    }
};
