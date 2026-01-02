using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace APHKLogicExtractor.ExtractorComponents.DataExtractor
{
    internal class IdFactory(ILogger<IdFactory> logger)
    {
        // we are using a byte (8 bits) for connection IDs. Since IDs must be <=int.Max (2^31 - 1) we get 31 bits
        // (don't forget 2^0 takes a bit) and 31-8 is 23.
        const int ITEM_BITS = 23;

        public async Task<Dictionary<string, int>> CreateIds(byte connectionId, IEnumerable<string> values, Dictionary<string, int> multiplicity)
        {
            int connectionPart = connectionId << ITEM_BITS;
            int idMask = (1 << ITEM_BITS) - 1;

            // cheating our duplicate detection a bit here - 0 is reserved so treat it as already chosen (by someone else)
            HashSet<int> selectedIds = [0];
            Dictionary<string, int> nameToIdMapping = [];
            IEnumerable<string> valuesWithMultiplicity = values.SelectMany(v =>
            {
                if (multiplicity.TryGetValue(v, out int count))
                {
                    return EnumerateWithMultiplicity(v, count);
                }
                else
                {
                    return [v];
                }
            });
            foreach (string val in valuesWithMultiplicity.ToHashSet().Order())
            {
                using MD5 md5 = MD5.Create();
                byte[] valBytes = Encoding.UTF8.GetBytes(val);
                using MemoryStream ms = new(valBytes);
                byte[] hash = await md5.ComputeHashAsync(ms);
                int proposedId = 0;
                int bitsRemaining = ITEM_BITS;
                for (int i = 0; bitsRemaining > 0; i++)
                {
                    int bitsToFetch = Math.Min(bitsRemaining, 8);
                    // 1. bitmask for the lower n bits, 2^n - 1
                    byte mask = (byte)((1 << bitsToFetch) - 1);
                    // 2. get the bits
                    byte bitsFromHash = (byte)(hash[i] & mask);
                    // 3. make space and add the bits
                    proposedId = (proposedId << bitsToFetch) | bitsFromHash;
                    bitsRemaining -= bitsToFetch;
                }
                // sanity check - we should have exactly spent all the bits available
                if (bitsRemaining != 0)
                {
                    logger.LogError("Overspent bits during ID generation");
                }

                int finalId = Deduplicate(selectedIds, proposedId) & idMask;
                selectedIds.Add(finalId);
                int compositeId = connectionPart | finalId;
                nameToIdMapping.Add(val, compositeId);
            }
            // sanity check - all ids should be unique
            if (nameToIdMapping.Values.Distinct().Count() != nameToIdMapping.Count)
            {
                logger.LogError("Generated duplicate IDs for connection ID {}", connectionId);
            }
            // sanity check - all ids should be 0 < x < 2^31 (max bound is enforced by typing)
            foreach (KeyValuePair<string, int> pair in nameToIdMapping)
            {
                if (pair.Value < 1)
                {
                    logger.LogError("ID {} for item {} in connection {} was outside the expected range", pair.Value, pair.Key, connectionId);
                }
            }

            return nameToIdMapping;
        }

        private int Deduplicate(HashSet<int> selectedIds, int newId)
        {
            while (selectedIds.Contains(newId))
            {
                logger.LogWarning("Hash collision found ({})", newId);
                newId++;
            }
            return newId;
        }

        private IEnumerable<string> EnumerateWithMultiplicity(string value, int count)
        {
            for (int i = 1; i <= count; i++)
            {
                yield return $"{value}_{i}";
            }
        }
    }
}
