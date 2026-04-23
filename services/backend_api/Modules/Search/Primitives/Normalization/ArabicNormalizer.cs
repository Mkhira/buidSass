using System.Text;
using System.Text.RegularExpressions;

namespace BackendApi.Modules.Search.Primitives.Normalization;

public sealed class ArabicNormalizer
{
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);

    public string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (IsDiacritic(ch) || ch == '\u0640')
            {
                continue;
            }

            var mapped = ch switch
            {
                'آ' or 'إ' or 'أ' or 'ٱ' or 'ا' => 'ا',
                'ي' or 'ى' or 'ئ' => 'ي',
                'ه' or 'ة' => 'ه',
                >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                _ => ch,
            };

            if (IsCommonPunctuation(mapped))
            {
                builder.Append(' ');
                continue;
            }

            builder.Append(char.ToLowerInvariant(mapped));
        }

        return WhitespaceRegex.Replace(builder.ToString(), " ").Trim();
    }

    private static bool IsDiacritic(char ch)
    {
        return ch is >= '\u064B' and <= '\u0652' or '\u0670';
    }

    private static bool IsCommonPunctuation(char ch)
    {
        return ch is '.' or ',' or ';' or ':' or '!' or '?' or '-' or '_' or '/' or '\\'
            or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '\''
            or '،' or '؛' or '؟' or '«' or '»' or '“' or '”' or '’';
    }
}
