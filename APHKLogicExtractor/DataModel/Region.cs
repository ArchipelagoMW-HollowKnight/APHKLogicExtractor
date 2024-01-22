using Newtonsoft.Json;

namespace APHKLogicExtractor.DataModel
{
    internal record Region(string Name)
    {
        [JsonConverter(typeof(ConnectionSerializer))]
        public record Connection(
            HashSet<string> ItemRequirements,
            HashSet<string> LocationRequirements,
            List<string> StateModifiers,
            Region Target);

        private List<Connection> exits = new();
        public IReadOnlyList<Connection> Exits => exits;

        public HashSet<string> Locations { get; } = new();

        public void Connect(
            IEnumerable<string> requirements,
            IEnumerable<string> locationRequirements,
            IEnumerable<string> stateModifiers,
            Region target)
        {
            exits.Add(new Connection(
                requirements.ToHashSet(),
                locationRequirements.ToHashSet(),
                stateModifiers.ToList(),
                target
            ));
        }

        public void Disconnect(Region target)
        {
            exits.RemoveAll(x => x.Target == target);
        }
    }

    internal class ConnectionSerializer : JsonConverter<Region.Connection>
    {
        public override bool CanRead => false;

        public override Region.Connection? ReadJson(
            JsonReader reader,
            Type objectType,
            Region.Connection? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, Region.Connection? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                serializer.Serialize(writer, null);
                return;
            }

            writer.WriteStartObject();
            writer.WritePropertyName(nameof(value.Target));
            writer.WriteValue(value.Target.Name);
            writer.WritePropertyName(nameof(value.ItemRequirements));
            serializer.Serialize(writer, value.ItemRequirements);
            writer.WritePropertyName(nameof(value.LocationRequirements));
            serializer.Serialize(writer, value.LocationRequirements);
            writer.WritePropertyName(nameof(value.StateModifiers));
            serializer.Serialize(writer, value.StateModifiers);
            writer.WriteEndObject();
        }
    }
}
