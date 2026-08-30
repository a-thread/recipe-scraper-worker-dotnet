using RecipeScraper.Infrastructure.Parsing;

namespace RecipeScraper.Tests;

public class AngleSharpRecipeParserTests
{
    private const string BaseUrl = "https://example.com/recipes/pancakes";
    private static readonly AngleSharpRecipeParser Parser = new();

    [Fact]
    public void ExtractsTitleDescriptionResolvedImageTimesServingsIngredientsAndSteps()
    {
        var html = """
            <html>
                <head>
                    <meta property="og:image" content="/images/pancakes.jpg">
                    <meta name="description" content="Fluffy pancakes.">
                </head>
                <body>
                    <h1 class="recipe-title">Fluffy Pancakes</h1>
                    <span class="prep-time">10 minutes</span>
                    <span class="cook-time">1 hour</span>
                    <span class="servings">4 servings</span>
                    <ul class="ingredients">
                        <li>2 cups flour</li>
                        <li>1 &frac12; cups milk</li>
                    </ul>
                    <ol class="instructions">
                        <li>Mix dry ingredients.</li>
                        <li>Add milk and whisk.</li>
                    </ol>
                </body>
            </html>
            """;

        var recipe = Parser.Parse(html, BaseUrl);

        Assert.Equal("Fluffy Pancakes", recipe.Title);
        Assert.Equal("Fluffy pancakes.", recipe.Description);
        Assert.Equal("https://example.com/images/pancakes.jpg", recipe.ImgUrl);
        Assert.Equal(10, recipe.PrepTime);
        Assert.Equal(60, recipe.CookTime);
        Assert.Equal(4, recipe.Servings);
        Assert.Equal(["2 cups flour", "1 1/2 cups milk"], recipe.Ingredients.Select(i => i.Value));
        Assert.Equal(["Mix dry ingredients.", "Add milk and whisk."], recipe.Steps.Select(s => s.Value));
        Assert.Equal(BaseUrl, recipe.OriginalRecipeUrl);
    }

    [Fact]
    public void FallsBackToUntitledRecipeWhenNoTitleIsFound()
    {
        var recipe = Parser.Parse("<html><body></body></html>", BaseUrl);
        Assert.Equal("Untitled Recipe", recipe.Title);
    }

    [Fact]
    public void GroupsIngredientsFromAStructuralWprmStyleContainer()
    {
        var html = """
            <div class="wprm-recipe-ingredient-group">
                <span class="wprm-recipe-ingredient-group-name">For the topping</span>
                <ul>
                    <li class="wprm-recipe-ingredient">1 cup berries</li>
                    <li class="wprm-recipe-ingredient">2 tbsp sugar</li>
                </ul>
            </div>
            """;

        var recipe = Parser.Parse(html, BaseUrl);

        Assert.Collection(recipe.Ingredients,
            i => Assert.Equal(("1 cup berries", "For the topping"), (i.Value, i.Group)),
            i => Assert.Equal(("2 tbsp sugar", "For the topping"), (i.Value, i.Group)));
    }

    [Fact]
    public void GroupsIngredientsFromAnInlineHeadingLiTastyRecipesStyle()
    {
        var html = """
            <ul class="ingredients">
                <li><strong>For the batter:</strong></li>
                <li>2 cups flour</li>
                <li>For the topping:</li>
                <li>1 cup berries</li>
            </ul>
            """;

        var recipe = Parser.Parse(html, BaseUrl);

        Assert.Collection(recipe.Ingredients,
            i => Assert.Equal(("2 cups flour", "For the batter"), (i.Value, i.Group)),
            i => Assert.Equal(("1 cup berries", "For the topping"), (i.Value, i.Group)));
    }

    [Fact]
    public void StripsStepNumberBadgesWithoutLeakingTheirTextIntoTheStep()
    {
        var html = """
            <ol class="instructions">
                <li>
                    <div class="stepNumber">Step 1</div>
                    <div class="stepContent">Preheat the oven.</div>
                </li>
            </ol>
            """;

        var recipe = Parser.Parse(html, BaseUrl);

        Assert.Equal(["Preheat the oven."], recipe.Steps.Select(s => s.Value));
    }

    [Fact]
    public void PreservesWhitespaceBetweenAdjacentInlineElementsWhenStrippingStepHtml()
    {
        var html = """
            <ol class="instructions">
                <li>Preheat<br>the oven to <strong>350&deg;F</strong>.</li>
            </ol>
            """;

        var recipe = Parser.Parse(html, BaseUrl);

        Assert.Equal(["Preheat the oven to 350°F ."], recipe.Steps.Select(s => s.Value));
    }
}
