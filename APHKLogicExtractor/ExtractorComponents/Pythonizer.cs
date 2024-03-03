using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace APHKLogicExtractor.ExtractorComponents
{
    internal class Pythonizer
    {
        private string PascalToSnakeCase(string pascalCase)
        {
            return string.Concat(pascalCase.Select((x, i) => i > 0 && char.IsUpper(x) ? $"_{x}" : x.ToString())).ToLower();
        }

        public void Write<T>(T obj, TextWriter writer)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }
            JObject root = JObject.FromObject(obj);

            writer.WriteLine("# This file is programmatically generated, do not modify by hand");
            writer.WriteLine();
            writer.WriteLine();

            foreach (var (key, value) in root)
            {
                WriteRootObject(key, value, writer);
                writer.WriteLine();
            }
        }

        private void WriteRootObject(string? key, JToken? value, TextWriter writer)
        {
            if (key == null || value == null)
            {
                return;
            }
            writer.Write($"{PascalToSnakeCase(key)} = ");
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
                WriteString(PascalToSnakeCase(item.Key), writer);
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
