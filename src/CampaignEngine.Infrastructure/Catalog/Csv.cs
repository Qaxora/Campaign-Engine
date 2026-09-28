using System.Text;

namespace CampaignEngine.Infrastructure.Catalog;

/// <summary>
/// Small RFC 4180 reader: quoted fields, escaped quotes (<c>""</c>), line breaks inside quotes.
/// The delimiter (<c>,</c> <c>;</c> or tab) is detected from the header line, because spreadsheets in
/// Turkish locales export with <c>;</c>.
/// </summary>
public static class Csv
{
    public static async Task<List<string[]>> ReadAsync(TextReader reader, int maxRows, CancellationToken cancellationToken = default)
    {
        var text = await reader.ReadToEndAsync(cancellationToken);
        var delimiter = DetectDelimiter(text);
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRow();
            }
            else
            {
                field.Append(c);
            }
        }

        EndRow();
        return rows;

        void EndRow()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Exists(f => f.Trim().Length > 0))
            {
                if (rows.Count >= maxRows + 1) // + header
                {
                    throw new Services.ValidationException([$"At most {maxRows} rows per import."]);
                }

                rows.Add([.. fields.Select(f => f.Trim())]);
            }

            fields.Clear();
        }
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return new[] { ',', ';', '\t' }.MaxBy(d => header.Count(c => c == d));
    }
}
