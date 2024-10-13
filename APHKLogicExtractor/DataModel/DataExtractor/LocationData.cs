namespace APHKLogicExtractor.DataModel.DataExtractor
{
    internal record LocationDetails(string MapArea, string TitledArea);

    internal record LocationData(Dictionary<string, LocationDetails> Locations, List<string> MultiLocations);
}
