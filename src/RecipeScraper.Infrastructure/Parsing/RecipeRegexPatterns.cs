using System.Text.RegularExpressions;

namespace RecipeScraper.Infrastructure.Parsing;

// Regex patterns used by AngleSharpRecipeParser, grouped here so they can be scanned and
// tuned without wading through parsing logic.
public sealed partial class AngleSharpRecipeParser
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRegex();

    [GeneratedRegex("hour", RegexOptions.IgnoreCase)]
    private static partial Regex HourRegex();

    [GeneratedRegex(@"(^|\s)[\w-]*ingredient-group(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex IngredientGroupRegex();

    [GeneratedRegex("(step|instruction)[-_]?number", RegexOptions.IgnoreCase)]
    private static partial Regex StepNumberRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();

    [GeneratedRegex("[▢☐□]")]
    private static partial Regex CheckboxGlyphRegex();

    [GeneratedRegex(@"(\d+)([¼½¾⅓⅔⅕⅖⅗⅘])")]
    private static partial Regex NumberFractionRegex();

    [GeneratedRegex("[¼½¾⅓⅔⅕⅖⅗⅘]")]
    private static partial Regex FractionCharRegex();

    [GeneratedRegex("([0-9/]+)([a-zA-Z])")]
    private static partial Regex NumberAlphaRegex();
}
