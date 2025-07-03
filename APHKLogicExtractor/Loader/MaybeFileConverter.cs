using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace APHKLogicExtractor.Loader;

internal class MaybeFileConverter(ResourceLoader resourceLoader) : JsonConverter
{
    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType)
    {
        return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(MaybeFile<>);
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
            object? instance = Activator.CreateInstance(objectType, true);
            objectType.GetProperty("Lazy")?.SetValue(instance, (resourceLoader, reader.Value as string));
            return instance;
        }

        // We detected an object or array in the JSON, therefore, we'll try to parse as usual.
        if (reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.StartArray)
        {
            // Grabs `MaybeFile` generic argument `T`
            Type innerType = objectType.GetGenericArguments()[0];

            object? deserialized = serializer.Deserialize(reader, innerType);
            object? instance = Activator.CreateInstance(objectType, true);
            objectType.GetField("content", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(instance, new ResourceLock<object?>(deserialized));
            if (innerType == typeof(JToken))
            {
                objectType.GetProperty(nameof(MaybeFile<object>.Serializer))?.SetValue(instance, serializer);
            }

            return instance;
        }

        throw new JsonSerializationException($"Unable to deserialize token {reader.TokenType}.");
    }

    public override void WriteJson(
        JsonWriter writer,
        object? value,
        JsonSerializer serializer)
    {
        throw new InvalidOperationException();
    }
}

internal class MaybeFile<T> where T : class
{
    public (ResourceLoader loader, string uri)? Lazy { get; set; }
    public JsonSerializer? Serializer { get; set; }

    private ResourceLock<object?> content = new(null);

    [JsonConstructor]
    private MaybeFile() { }

    public MaybeFile(T content)
    {
        this.content = new(content);
    }

    public Task<T> GetContent()
    {
        return this.GetContent<T>();
    }

    public async Task<U> GetContent<U>() where U : class
    {
        using ResourceLock<object?>.LockGuard guard = await this.content.Enter(TimeSpan.FromSeconds(30));

        if (this.Lazy != null)
        {
            (ResourceLoader loader, string uri) = this.Lazy.Value;
            ResourceLoader.Content rawContent = await loader.Load(uri);

            guard.Value = rawContent.AsJson<U>();
            this.Lazy = null;
        }

        if (guard.Value is U data)
        {
            return data;
        }

        if (this.Serializer != null && guard.Value is JToken tokens)
        {
            return tokens.ToObject<U>(this.Serializer)
                ?? throw new InvalidCastException($"Unable to cast content to the given type: {typeof(U)} from null");
        }

        throw new InvalidCastException($"Unable to cast content to the given type: {typeof(U)} from {guard.Value?.GetType().ToString() ?? "null"}");
    }
}
