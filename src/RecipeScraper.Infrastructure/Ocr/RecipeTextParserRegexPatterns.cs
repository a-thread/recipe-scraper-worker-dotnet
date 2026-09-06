using System.Text.RegularExpressions;

namespace RecipeScraper.Infrastructure.Ocr;

public sealed partial class RecipeTextParser
{
    [GeneratedRegex(@"^ingredients?\s*[:.]?$", RegexOptions.IgnoreCase)]
    private static partial Regex IngredientsHeaderRegex();

    [GeneratedRegex(@"^(instructions?|directions?|method|steps?)\s*[:.]?$", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionsHeaderRegex();

    [GeneratedRegex(@"^\d{1,3}$")]
    private static partial Regex PageNumberOnlyRegex();

    [GeneratedRegex(@"^\(?(\d{1,2})[.):]\s+")]
    private static partial Regex StepMarkerRegex();

    [GeneratedRegex(@"prep(?:aration)?\s*time\s*:?\s*([^\n]{1,30})", RegexOptions.IgnoreCase)]
    private static partial Regex PrepTimeLabelRegex();

    [GeneratedRegex(@"cook(?:ing)?\s*time\s*:?\s*([^\n]{1,30})", RegexOptions.IgnoreCase)]
    private static partial Regex CookTimeLabelRegex();

    [GeneratedRegex(@"(?:servings?|serves|yield)\s*:?\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ServingsRegex();

    [GeneratedRegex(@"(\d+)\s*h(?:ou)?rs?", RegexOptions.IgnoreCase)]
    private static partial Regex HoursRegex();

    [GeneratedRegex(@"(\d+)\s*m(?:in)?s?", RegexOptions.IgnoreCase)]
    private static partial Regex MinutesRegex();

    [GeneratedRegex(@"^\s*(\d+)")]
    private static partial Regex BareNumberRegex();

    [GeneratedRegex(@"[▢☐□]")]
    private static partial Regex CheckboxGlyphRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();

    [GeneratedRegex(@"(\d+)([¼½¾⅓⅔⅕⅖⅗⅘])")]
    private static partial Regex NumberFractionRegex();

    [GeneratedRegex(@"[¼½¾⅓⅔⅕⅖⅗⅘]")]
    private static partial Regex FractionCharRegex();

    [GeneratedRegex(@"([0-9/]+)([a-zA-Z])")]
    private static partial Regex NumberAlphaRegex();
}
