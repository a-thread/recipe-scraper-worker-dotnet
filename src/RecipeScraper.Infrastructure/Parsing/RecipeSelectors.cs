namespace RecipeScraper.Infrastructure.Parsing;

// CSS selectors and related constants used by AngleSharpRecipeParser, grouped here so the
// site-matching heuristics can be scanned and tuned without wading through parsing logic.
public sealed partial class AngleSharpRecipeParser
{
    private static readonly string[] ImageSelectors =
    [
        "meta[property='og:image']",
        "meta[name='og:image']",
        "meta[itemprop='image']",
        "img[class*='recipe-image']",
        "img[class*='main-image']",
        "img",
    ];

    private static readonly string[] DescriptionSelectors =
    [
        "meta[name='description']",
        "meta[property='og:description']",
        "meta[name='twitter:description']",
        "*[class*='recipe-summary']",
    ];

    private static readonly string[] TitleSelectors = ["h1.recipe-title", "h1", "h2"];

    private static readonly string[] PrepTimeSelectors = ["*[class*='prep_time'], *[class*='prep-time']"];

    private static readonly string[] CookTimeSelectors = ["*[class*='cook_time'], *[class*='cook-time']"];

    private static readonly string[] ServingsSelectors = ["*[class*='servings']", "*[class*='yield']"];

    private static readonly string[] StepSelectors =
    [
        "ol[class*='instructions'] li, ul[class*='instructions'] li",
        "div[class*='instructions'] li",
        "div[class*='instructions'] div[class*='step']",
        "ol[class*='preparation'] li",
        "div[class*='steps'] ol li",
    ];

    private static readonly string[] IngredientSelectors =
        ["ul[class*='ingredients'] li", "ol[class*='ingredients'] li", "div[class*='ingredients'] li"];

    private const string IngredientGroupContainerSelector = "[class*='ingredient-group']";

    private const string IngredientGroupNameSelector = "[class*='group-name'], [class*='group-heading']";

    private const string NamedIngredientItemSelector = "li[class*='ingredient']";

    private const string StepNumberBadgeSelector = "[class]";

    // Real ingredient group headings (e.g. "For the topping") run short; this bounds how much
    // text ExtractInlineGroupHeading will consider before assuming a line isn't a heading.
    private const int MaxGroupHeadingLength = 40;
}
