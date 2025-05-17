using Newtonsoft.Json;

namespace APHKLogicExtractor.DataModel
{
    internal record RequirementBranch(
        HashSet<string> ItemRequirements,
        HashSet<string> LocationRequirements,
        HashSet<string> RegionRequirements,
        List<string> StateModifiers)
    {
        [JsonIgnore]
        public bool IsEmpty => ItemRequirements.Count == 0
            && LocationRequirements.Count == 0
            && RegionRequirements.Count == 0
            && StateModifiers.Count == 0;

        public static RequirementBranch operator +(RequirementBranch lhs, RequirementBranch rhs)
        {
            return new RequirementBranch(
                [.. lhs.ItemRequirements, .. rhs.ItemRequirements],
                [.. lhs.LocationRequirements, .. rhs.LocationRequirements],
                [.. lhs.RegionRequirements, .. rhs.RegionRequirements],
                [.. lhs.StateModifiers, .. rhs.StateModifiers]);
        }
    }
}
