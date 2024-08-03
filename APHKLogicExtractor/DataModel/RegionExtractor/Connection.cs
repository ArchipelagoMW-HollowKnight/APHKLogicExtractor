using Newtonsoft.Json;

namespace APHKLogicExtractor.DataModel
{
    internal record Connection(
        List<RequirementBranch> Logic,
        [property: JsonConverter(typeof(TargetSerializer))] Region Target): IGraphLogicObject;

    internal class TargetSerializer : JsonConverter<Region>
    {
        public override bool CanRead => false;

        public override Region? ReadJson(JsonReader reader, Type objectType, Region? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, Region? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                serializer.Serialize(writer, null);
                return;
            }

            serializer.Serialize(writer, value.Name);
        }
    }
}
