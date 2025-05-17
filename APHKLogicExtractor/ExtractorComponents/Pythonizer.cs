using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace APHKLogicExtractor.ExtractorComponents
{
    internal partial class Pythonizer
    {
        [GeneratedRegex(@"[^a-zA-Z0-9_]")]
        private static partial Regex BadNameCharFinder();

        [GeneratedRegex(@"_+")]
        private static partial Regex DoubleUnderscoreFinder();

        public string PythonizeName(string name, bool upper = false)
        {
            if (upper)
            {
                name = name.ToUpperInvariant();
            }
            else
            {
                name = name.ToLowerInvariant();
            }
            name = name.Replace("'", "");
            name = BadNameCharFinder().Replace(name, "_");
            name = DoubleUnderscoreFinder().Replace(name, "_");
            name = name.Trim('_');
            name = name.TrimStart([.. "0123456789"]);
            return name;
        }

        public void WriteEnum(string name, IEnumerable<string> values, TextWriter writer)
        {
            WriteHeader(writer);

            writer.WriteLine("""
                try:
                    from enum import StrEnum
                except ImportError:
                    from enum import Enum

                    class StrEnum(str, Enum):
                        pass

                """);
            writer.WriteLine();

            writer.WriteLine($"class {name}(StrEnum):");
            foreach (var value in values)
            {
                writer.WriteLine($"    {PythonizeName(value, true)} = {JsonConvert.SerializeObject(value)}");
            }
        }

        public void Write<T>(T obj, TextWriter writer)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }
            IContractResolver contractResolver = new DefaultContractResolver()
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            };
            JsonSerializer serializer = new()
            {
                ContractResolver = contractResolver,
                Formatting = Formatting.Indented,
                ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
            };
            JObject root = JObject.FromObject(obj, serializer);

            WriteHeader(writer);

            foreach (var (key, value) in root)
            {
                WriteRootObject(key, value, writer);
                writer.WriteLine();
            }
        }

        private void WriteHeader(TextWriter writer)
        {
            writer.WriteLine("# This file is programmatically generated, do not modify by hand");
            writer.WriteLine();
        }

        private void WriteRootObject(string? key, JToken? value, TextWriter writer)
        {
            if (key == null || value == null)
            {
                return;
            }
            writer.Write($"{key} = ");
            WriteUnknownToken(value, writer, 0);
        }

        private void WriteUnknownToken(JToken? value, TextWriter writer, int indentation)
        {
            if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
            {
                WriteNone(writer);
            }
            else if (value.Type == JTokenType.Object)
            {
                WriteObject((JObject)value, writer, indentation);
            }
            else if (value.Type == JTokenType.Array)
            {
                WriteArray((JArray)value, writer, indentation);
            }
            else
            {
                WriteLiteral((JValue)value, writer);
            }
        }

        private void WriteNone(TextWriter writer)
        {
            writer.Write("None");
        }

        private void WriteContainer<T>(
            IEnumerable<T> container,
            TextWriter writer,
            int indentation,
            char open,
            char close,
            Action<T> writeItem)
        {
            writer.Write(open);
            bool first = true;
            foreach (T item in container)
            {
                if (!first)
                {
                    writer.Write(",");
                }
                writer.WriteLine();
                Indent(writer, indentation + 1);
                writeItem(item);
                first = false;
            }
            // if there's no items keep the closing bracket on the same line
            if (!first)
            {
                writer.WriteLine();
                Indent(writer, indentation);
            }
            writer.Write(close);
        }

        private void WriteObject(JObject obj, TextWriter writer, int indentation)
        {
            WriteContainer<KeyValuePair<string, JToken?>>(obj, writer, indentation, '{', '}', item =>
            {
                WriteString(item.Key, writer);
                writer.Write(": ");
                WriteUnknownToken(item.Value, writer, indentation + 1);
            });
        }

        private void WriteArray(JArray arr, TextWriter writer, int indentation)
        {
            WriteContainer<JToken?>(arr, writer, indentation, '[', ']', item =>
            {
                WriteUnknownToken(item, writer, indentation + 1);
            });
        }

        private void WriteLiteral(JValue val, TextWriter writer)
        {
            if (val.Type == JTokenType.Boolean)
            {
                WriteBool(val.Value<bool>(), writer);
            }
            else if (val.Type == JTokenType.Integer || val.Type == JTokenType.Float)
            {
                WriteNumber(val.Value<double>(), writer);
            }
            else if (val.Type == JTokenType.String)
            {
                WriteString(val.Value<string>(), writer);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private void WriteBool(bool val, TextWriter writer)
        {
            writer.Write(val ? "True" : "False");
        }

        private void WriteNumber(double val, TextWriter writer)
        {
            writer.Write(val.ToString(CultureInfo.InvariantCulture));
        }

        private void WriteString([NotNull] string? val, TextWriter writer)
        {
            if (val == null)
            {
                throw new ArgumentNullException(nameof(val));
            }
            writer.Write($"\"{val}\"");
        }

        private void Indent(TextWriter writer, int indentation)
        {
            for (int i = 0; i < indentation; i++)
            {
                writer.Write("    ");
            }
        }
    }
}
