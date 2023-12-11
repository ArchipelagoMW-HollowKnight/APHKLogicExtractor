using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace APHKLogicExtractor.DataModel
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Direction
    {
        Left, Right, Top, Bot, Door
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum Sidedness
    {
        Both, OneWayOut, OneWayIn
    }

    internal record TransitionDef
    {
        public required string SceneName { get; set; }
        public required string DoorName { get; set; }
        public required string VanillaTarget { get; set; }
        public required Direction Direction { get; set; }
        public required bool IsTitledAreaTransition { get; set; }
        public required bool IsMapAreaTransition { get; set; }
        public required Sidedness Sides { get; set; }
    }
}
