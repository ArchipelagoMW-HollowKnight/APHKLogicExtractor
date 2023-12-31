﻿using RandomizerCore.Logic;

namespace APHKLogicExtractor.ExtractorComponents.RegionExtractor
{
    internal class VariableParser
    {
        public string GetPrefix(string term)
        {
            int x = term.IndexOf('[');
            int y = term.IndexOf(']');
            // various possible problem cases why an arbitrary string might have this operation well-defined.
            // Should not ever come up in theory but better to explode if it did
            if (y < x || (y != -1 && y < term.Length - 1) || x == 0 || (x == -1 && y != -1))
            {
                throw new ArgumentException("Not a valid term to find prefix", nameof(term));
            }
            if (x == -1)
            {
                return term;
            }
            return term[0..x];
        }

        public (string prefix, string[] parameters) Parse(string term)
        {
            string prefix = GetPrefix(term);
            if (VariableResolver.TryMatchPrefix(term, prefix, out string[]? parameters))
            {
                return (prefix, parameters);
            }
            throw new ArgumentException("Not a valid term to parse variable", nameof(term));
        }
    }
}
