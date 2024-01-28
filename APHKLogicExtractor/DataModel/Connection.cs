using Newtonsoft.Json;

namespace APHKLogicExtractor.DataModel
{
    [JsonConverter(typeof(ConnectionSerializer))]
    internal record Connection(
        List<RequirementBranch> Logic,
        Region Target): IGraphLogicObject;

    internal class ConnectionSerializer : JsonConverter<Connection>
    {
        public override bool CanRead => false;

        public override Connection? ReadJson(
            JsonReader reader,
            Type objectType,
            Connection? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, Connection? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                serializer.Serialize(writer, null);
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.Target));
            writer.WriteValue(value.Target.Name);
            writer.WritePropertyName(nameof(value.Logic));
            serializer.Serialize(writer, value.Logic);
            writer.WriteEndObject();
        }
    }
}
