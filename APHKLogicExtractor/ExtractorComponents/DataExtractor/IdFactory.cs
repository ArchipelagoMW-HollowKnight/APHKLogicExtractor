using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace APHKLogicExtractor.ExtractorComponents.DataExtractor
{
    internal class IdFactory(ILogger<IdFactory> logger)
    {
        public async Task<Dictionary<string, long>> CreateIds(uint connectionId, IEnumerable<string> values)
        {
            // upper bound id is 2**53 - 1 and the lower order 32 bits are for the generated id, which gives
            // us the other 21
            const uint upperBoundConnectionId = (1 << 21) - 1;

            if (connectionId > upperBoundConnectionId)
            {
                throw new ArgumentException($"Connection ID cannot be larger than {upperBoundConnectionId}", nameof(connectionId));
            }

            // cheating our duplicate detection a bit here - 0 is reserved so treat it as already chosen (by someone else)
            HashSet<uint> selectedIds = [0];
            Dictionary<string, long> nameToIdMapping = [];
            foreach (string val in values.ToHashSet().Order())
            {
                using MD5 md5 = MD5.Create();
                byte[] valBytes = Encoding.UTF8.GetBytes(val);
                using MemoryStream ms = new(valBytes);
                byte[] hash = await md5.ComputeHashAsync(ms);
                // ensure little endian byte order - most modern architectures are little endian
                // so this was chosen as the common denominator
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(hash);
                }
                uint proposedId = BitConverter.ToUInt32(hash, 0);
                uint finalId = Deduplicate(selectedIds, proposedId);
                selectedIds.Add(finalId);
                long compositeId = ((long)connectionId << 32) | finalId;
                nameToIdMapping.Add(val, compositeId);
            }
            // sanity check - all ids should be unique
            if (nameToIdMapping.Values.Distinct().Count() != nameToIdMapping.Count)
            {
                logger.LogError("Generated duplicate IDs for connection ID {}", connectionId);
            }
            return nameToIdMapping;
        }

        private uint Deduplicate(HashSet<uint> selectedIds, uint newId)
        {
            while (selectedIds.Contains(newId))
            {
                newId++;
            }
            return newId;
        }
    }
}
