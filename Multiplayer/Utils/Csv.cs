using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace Multiplayer.Utils;

public static class Csv
{
    /// <summary>
    ///     Parses a CSV string into a dictionary of columns, each of which is a dictionary of rows, keyed by the first column.
    /// </summary>
    public static ReadOnlyDictionary<string, Dictionary<string, string>> Parse(string data)
    {
        string[] separators = new string[]{"\r\n" };
        string[] lines = data.Split(separators, StringSplitOptions.None);

        // Dictionary<string, Dictionary<string, string>>
        OrderedDictionary columns = new(lines.Length - 1);

        List<string> keys = ParseLine(lines[0]);
        foreach (string key in keys)
            columns.Add(key, new Dictionary<string, string>());

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            List<string> values = ParseLine(line);
            if (values.Count == 0 || string.IsNullOrWhiteSpace(values[0]))
                continue;
            string key = values[0];
            for (int j = 0; j < values.Count; j++)
                ((Dictionary<string, string>)columns[j]).Add(key, values[j]);
        }

        return new ReadOnlyDictionary<string, Dictionary<string, string>>(columns.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (Dictionary<string, string>)entry.Value));
    }

    private static List<string> ParseLine(string line)
    {
        bool inQuotes = false;
        bool wasBackslash = false;
        List<string> values = new();
        StringBuilder builder = new();

        void FinishLine()
        {
            values.Add(builder.ToString());
            builder.Clear();
        }

        foreach (char c in line)
        {
            if (c == '\n' || (!inQuotes && c == ','))
            {
                FinishLine();
                continue;
            }

            switch (c)
            {
                case '\r':
                    Multiplayer.LogWarning("Encountered carriage return in CSV! Please use Unix-style line endings (LF).");
                    continue;
                case '"':
                    inQuotes = !inQuotes;
                    continue;
                case '\\':
                    wasBackslash = true;
                    continue;
            }

            if (wasBackslash)
            {
                wasBackslash = false;
                if (c == 'n')
                {
                    builder.Append('\n');
                    continue;
                }

                // Not a special character, so just append the backslash
                builder.Append('\\');
            }

            builder.Append(c);
        }

        if (builder.Length > 0)
            FinishLine();

        return values;
    }

    public static string Dump(ReadOnlyDictionary<string, Dictionary<string, string>> data)
    {
        StringBuilder result = new("\n");

        foreach (KeyValuePair<string, Dictionary<string, string>> column in data)
            result.Append($"{column.Key},");

        result.Remove(result.Length - 1, 1);
        result.Append('\n');

        int rowCount = data.Values.FirstOrDefault()?.Count ?? 0;

        for (int i = 0; i < rowCount; i++)
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> column in data)
                if (column.Value.Count > i)
                {
                    string value = column.Value.ElementAt(i).Value.Replace("\n", "\\n");
                    result.Append(value.Contains(',') ? $"\"{value}\"," : $"{value},");
                }
                else
                {
                    result.Append(',');
                }

            result.Remove(result.Length - 1, 1);
            result.Append('\n');
        }

        return result.ToString();
    }
}
