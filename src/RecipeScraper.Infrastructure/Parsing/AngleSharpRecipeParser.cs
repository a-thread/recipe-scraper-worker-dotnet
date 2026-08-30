using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using RecipeScraper.Core;
using RecipeScraper.Core.Abstractions;

namespace RecipeScraper.Infrastructure.Parsing;

public sealed partial class AngleSharpRecipeParser : IRecipeParser
{
    private static readonly string[] StepSelectors =
    [
        "ol[class*='instructions'] li, ul[class*='instructions'] li",
        "div[class*='instructions'] li",
        "div[class*='instructions'] div[class*='step']",
        "ol[class*='preparation'] li",
        "div[class*='steps'] ol li",
    ];

    public Recipe Parse(string html, string url)
    {
        var document = new HtmlParser().ParseDocument(html);

        var imgUrl = ResolveUrl(ExtractFirstMatch(document,
        [
            "meta[property='og:image']",
            "meta[name='og:image']",
            "meta[itemprop='image']",
            "img[class*='recipe-image']",
            "img[class*='main-image']",
            "img",
        ], "content", "src"), url);

        var description = ExtractFirstText(document,
        [
            "meta[name='description']",
            "meta[property='og:description']",
            "meta[name='twitter:description']",
            "*[class*='recipe-summary']",
        ]);

        var title = ExtractFirstText(document, ["h1.recipe-title", "h1", "h2"]);
        if (title.Length == 0) title = "Untitled Recipe";

        var prepTime = ExtractTime(document, ["*[class*='prep_time'], *[class*='prep-time']"]);
        var cookTime = ExtractTime(document, ["*[class*='cook_time'], *[class*='cook-time']"]);
        var servings = ExtractServings(document, ["*[class*='servings']", "*[class*='yield']"]);

        return new Recipe
        {
            Title = title,
            Description = StripHtml(description.Replace("\n", " ").Trim()),
            ImgUrl = imgUrl,
            PrepTime = prepTime,
            CookTime = cookTime,
            Servings = servings,
            Ingredients = GetIngredients(document),
            Steps = GetItems(document, StepSelectors, StripHtml),
            OriginalRecipeUrl = url,
        };
    }

    private static string ResolveUrl(string possibleUrl, string baseUrl)
    {
        if (string.IsNullOrEmpty(possibleUrl)) return "";
        return Uri.TryCreate(new Uri(baseUrl), possibleUrl, out var absolute) ? absolute.ToString() : possibleUrl;
    }

    private static string ExtractFirstMatch(IParentNode document, string[] selectors, string attr1, string attr2)
    {
        foreach (var selector in selectors)
        {
            var element = document.QuerySelector(selector);
            if (element is not null) return element.GetAttribute(attr1) ?? element.GetAttribute(attr2) ?? "";
        }
        return "";
    }

    // <meta> tags carry their value in the "content" attribute rather than as text content,
    // which a plain TextContent read would always report as empty.
    private static string ExtractFirstText(IParentNode document, string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var element = document.QuerySelector(selector);
            if (element is null) continue;
            var text = element.TagName.Equals("META", StringComparison.OrdinalIgnoreCase)
                ? element.GetAttribute("content") ?? ""
                : element.TextContent;
            text = text.Trim();
            if (text.Length > 0) return text;
        }
        return "";
    }

    private static int ExtractTime(IParentNode document, string[] selectors)
    {
        var combined = string.Join(", ", selectors);
        var labelElement = document.QuerySelectorAll(combined).FirstOrDefault(e => DigitRegex().IsMatch(e.TextContent));
        return ParseTimeByClass(labelElement);
    }

    private static int ExtractServings(IParentNode document, string[] selectors)
    {
        foreach (var selector in selectors)
        {
            var text = document.QuerySelectorAll(selector)
                .FirstOrDefault(e => DigitRegex().IsMatch(e.TextContent))
                ?.TextContent.Trim() ?? "";
            if (text.Length > 0)
            {
                var match = DigitRegex().Match(text);
                return match.Success ? int.Parse(match.Value) : 0;
            }
        }
        return 0;
    }

    private static IReadOnlyList<StepIngredient> GetIngredients(IParentNode document)
    {
        var structural = GetStructuralIngredientGroups(document);
        if (structural.Count > 0)
        {
            return structural.Select(i => new StepIngredient(Guid.NewGuid().ToString(), i.Value, i.Group)).ToList();
        }

        var ingredients = new List<(string Value, string? Group)>();
        string[] selectors = ["ul[class*='ingredients'] li", "ol[class*='ingredients'] li", "div[class*='ingredients'] li"];

        foreach (var selector in selectors)
        {
            string? currentGroup = null;
            foreach (var el in document.QuerySelectorAll(selector))
            {
                var groupHeading = ExtractInlineGroupHeading(el);
                if (groupHeading is not null)
                {
                    // The <li> is a sub-heading (e.g. "For the topping"), not an ingredient itself.
                    currentGroup = groupHeading;
                    continue;
                }
                var content = el.TextContent.Trim();
                if (content.Length > 0) ingredients.Add((ParseIngredient(content), currentGroup));
            }
            if (ingredients.Count > 0) break;
        }

        return ingredients.Select(i => new StepIngredient(Guid.NewGuid().ToString(), i.Value, i.Group)).ToList();
    }

    // WP Recipe Maker and similar plugins split ingredients into named sub-groups (e.g.
    // "For the topping") using a wrapping container per group — a sibling "group-name"
    // label next to that group's own <ul>/<ol> — rather than a marker inside a shared
    // list. Matches a container class like "wprm-recipe-ingredient-group" while excluding
    // the label itself, whose class ends in "-group-name"/"-group-heading".
    private static bool IsIngredientGroupContainer(string className) => IngredientGroupRegex().IsMatch(className);

    private static List<(string Value, string? Group)> GetStructuralIngredientGroups(IParentNode document)
    {
        var groupContainers = document.QuerySelectorAll("[class*='ingredient-group']")
            .Where(el => IsIngredientGroupContainer(el.GetAttribute("class") ?? ""))
            .ToList();
        if (groupContainers.Count == 0) return [];

        var ingredients = new List<(string Value, string? Group)>();
        foreach (var container in groupContainers)
        {
            var groupName = container.QuerySelector("[class*='group-name'], [class*='group-heading']")?.TextContent.Trim();
            var namedItems = container.QuerySelectorAll("li[class*='ingredient']");
            var items = namedItems.Length > 0 ? namedItems : container.QuerySelectorAll("li");
            foreach (var li in items)
            {
                var content = li.TextContent.Trim();
                if (content.Length > 0) ingredients.Add((ParseIngredient(content), string.IsNullOrEmpty(groupName) ? null : groupName));
            }
        }
        return ingredients;
    }

    // Sites without a structural group container (e.g. Tasty Recipes) instead render the
    // group name as its own <li> within the same list, either bare or wrapped only in
    // <strong>/<b>, ending in a colon and with no digits — real ingredient lines virtually
    // always carry a quantity.
    private static string? ExtractInlineGroupHeading(IElement el)
    {
        var text = el.TextContent.Trim();
        var children = el.Children;
        var onlyStrongChild = children.Length == 1 &&
            (children[0].TagName.Equals("STRONG", StringComparison.OrdinalIgnoreCase) ||
             children[0].TagName.Equals("B", StringComparison.OrdinalIgnoreCase));
        if ((onlyStrongChild || children.Length == 0) && text.EndsWith(':') && !DigitRegex().IsMatch(text) && text.Length < 40)
        {
            return text[..^1];
        }
        return null;
    }

    private static IReadOnlyList<StepIngredient> GetItems(IParentNode document, string[] selectors, Func<string, string> parser)
    {
        var items = new List<string>();
        foreach (var selector in selectors)
        {
            foreach (var el in document.QuerySelectorAll(selector))
            {
                var clone = (IElement)el.Clone(true);
                RemoveStepNumberBadges(clone);
                var content = clone.InnerHtml;
                if (!string.IsNullOrEmpty(content)) items.Add(parser(content));
            }
            if (items.Count > 0) break;
        }
        return items.Select(value => new StepIngredient(Guid.NewGuid().ToString(), value)).ToList();
    }

    // Some sites (e.g. NYT Cooking) render the step number as its own badge element
    // alongside the step text within the same list item, rather than via a CSS counter
    // — e.g. <div class="...stepNumber...">Step 1</div><div class="...stepContent...">...
    // Left in place, that badge's text ("Step 1") gets prepended to every step.
    private static void RemoveStepNumberBadges(IElement el)
    {
        foreach (var child in el.QuerySelectorAll("[class]").ToList())
        {
            var className = child.GetAttribute("class") ?? "";
            if (StepNumberRegex().IsMatch(className)) child.Remove();
        }
    }

    private static readonly Dictionary<char, string> FractionMap = new()
    {
        ['½'] = "1/2", ['⅓'] = "1/3", ['⅔'] = "2/3", ['¼'] = "1/4", ['¾'] = "3/4",
        ['⅕'] = "1/5", ['⅖'] = "2/5", ['⅗'] = "3/5", ['⅘'] = "4/5",
    };

    private static string ParseIngredient(string ingredientText)
    {
        var text = CheckboxGlyphRegex().Replace(ingredientText, ""); // strip WPRM's decorative checkbox glyph
        text = WhitespaceRunRegex().Replace(text, " "); // collapse newlines/runs of whitespace left over from nested quantity/unit/name/notes spans
        text = NumberFractionRegex().Replace(text, "$1 $2"); // add space between whole numbers and fractions
        text = FractionCharRegex().Replace(text, m => FractionMap[m.Value[0]]); // convert fraction special characters to normalized ones
        text = NumberAlphaRegex().Replace(text, "$1 $2"); // add space between numbers and alpha characters
        return text.Trim();
    }

    // Replaces (rather than deletes) tags first so adjacent elements/line breaks (e.g. <br>,
    // </p><p>) don't glue their text together, then lets AngleSharp decode entities like
    // &amp;/&nbsp; that a plain tag-strip would otherwise leave in the output verbatim.
    private static string StripHtml(string html)
    {
        var withBreaks = TagRegex().Replace(html, " ");
        var fragment = new HtmlParser().ParseDocument($"<div>{withBreaks}</div>");
        var decoded = fragment.Body?.TextContent ?? "";
        return WhitespaceRunRegex().Replace(decoded, " ").Trim();
    }

    private static int ParseTimeByClass(IElement? labelElement)
    {
        if (labelElement is null) return 0;
        var timeText = labelElement.TextContent.Trim();
        var match = DigitRegex().Match(timeText);
        var timeValue = match.Success ? int.Parse(match.Value) : 0;
        return HourRegex().IsMatch(timeText) ? timeValue * 60 : timeValue;
    }

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
