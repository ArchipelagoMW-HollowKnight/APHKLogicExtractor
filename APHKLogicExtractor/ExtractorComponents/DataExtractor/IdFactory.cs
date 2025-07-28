using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace APHKLogicExtractor.ExtractorComponents.DataExtractor
{
    internal class IdFactory(ILogger<IdFactory> logger)
    {
        // we are using a ushort (16 bits) for connection IDs. Since IDs must be <2^53, we have 53 bits to work with
        // (don't forget 2^0 takes a bit) and 53-16 is 37.
        const int ITEM_BITS = 37;

        public async Task<Dictionary<string, long>> CreateIds(ushort connectionId, IEnumerable<string> values, Dictionary<string, int> multiplicity)
        {
            long connectionMask = connectionId << ITEM_BITS;
            long idMask = (1 << ITEM_BITS) - 1;

            // cheating our duplicate detection a bit here - 0 is reserved so treat it as already chosen (by someone else)
            HashSet<long> selectedIds = [0];
            Dictionary<string, long> nameToIdMapping = [];
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
                long proposedId = 0;
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

                long finalId = Deduplicate(selectedIds, proposedId);
                selectedIds.Add(finalId);
                long compositeId = idMask | finalId;
                nameToIdMapping.Add(val, compositeId);
            }
            // sanity check - all ids should be unique
            if (nameToIdMapping.Values.Distinct().Count() != nameToIdMapping.Count)
            {
                logger.LogError("Generated duplicate IDs for connection ID {}", connectionId);
            }
            // sanity check - all ids should be 0 < x <2^53
            foreach (KeyValuePair<string, long> pair in nameToIdMapping)
            {
                if (pair.Value < 1 || (1L << 53) < pair.Value)
                {
                    logger.LogError("ID {} for item {} in connection {} was outside the expected range", pair.Value, pair.Key, connectionId);
                }
            }

            return nameToIdMapping;
        }

        private long Deduplicate(HashSet<long> selectedIds, long newId)
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
