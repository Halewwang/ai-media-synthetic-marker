using System.Text;

namespace Emke.AiMarker.Infrastructure.Tests.TestSupport;

internal static class CsvTestParser
{
    public static IReadOnlyList<string> Parse(string row)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        bool quoted = false;

        for (int index = 0; index < row.Length; index++)
        {
            char character = row[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < row.Length && row[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    value.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < row.Length && row[index + 1] == '\n')
                {
                    index++;
                }

                if (index != row.Length - 1)
                {
                    throw new InvalidOperationException("CSV row has data after its terminator.");
                }
            }
            else
            {
                value.Append(character);
            }
        }

        if (quoted)
        {
            throw new InvalidOperationException("CSV row has an unterminated quoted field.");
        }

        values.Add(value.ToString());
        return values;
    }
}
