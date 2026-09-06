using RecipeScraper.Infrastructure.Ocr;

namespace RecipeScraper.Tests;

public class OcrRecipeTextParserTests
{
    private static readonly OcrRecipeTextParser Parser = new();

    [Fact]
    public void ExtractsTitleIngredientsAndNumberedSteps()
    {
        const string text = """
            Spinach + Walnut Crumble Gnocchi

            Prep Time: 10 minutes
            Cook Time: 20 minutes
            Servings: 4

            Ingredients
            2 packages chestnut mushrooms
            1 package gnocchi
            1/3 cup of walnuts

            Instructions
            1. Add walnuts, nutritional yeast and miso paste to a food processor and blitz.
            2. Cook gnocchi according to the package.
            3. Drain the gnocchi and add to pan.
            """;

        var recipe = Parser.Parse(text);

        Assert.Equal("Spinach + Walnut Crumble Gnocchi", recipe.Title);
        Assert.Equal(10, recipe.PrepTime);
        Assert.Equal(20, recipe.CookTime);
        Assert.Equal(4, recipe.Servings);
        Assert.Equal(3, recipe.Ingredients.Count);
        Assert.Equal("2 packages chestnut mushrooms", recipe.Ingredients[0].Value);
        Assert.Equal(3, recipe.Steps.Count);
        Assert.Equal(
            "Add walnuts, nutritional yeast and miso paste to a food processor and blitz.",
            recipe.Steps[0].Value);
    }

    [Fact]
    public void DetectsIngredientGroupHeadings()
    {
        const string text = """
            Layered Dessert

            Ingredients
            For the base:
            2 cups flour
            1 cup sugar
            For the topping:
            1 cup cream

            Instructions
            1. Mix the base ingredients.
            """;

        var recipe = Parser.Parse(text);

        Assert.Equal("For the base", recipe.Ingredients[0].Group);
        Assert.Equal("For the base", recipe.Ingredients[1].Group);
        Assert.Equal("For the topping", recipe.Ingredients[2].Group);
    }

    [Fact]
    public void FallsBackToBlankLineParagraphsWhenNoNumberedMarkers()
    {
        const string text = """
            Simple Soup

            Ingredients
            2 cups broth

            Instructions
            Boil the broth in a large pot until simmering.

            Add the vegetables and cook for ten minutes.
            """;

        var recipe = Parser.Parse(text);

        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal("Boil the broth in a large pot until simmering.", recipe.Steps[0].Value);
        Assert.Equal("Add the vegetables and cook for ten minutes.", recipe.Steps[1].Value);
    }

    [Fact]
    public void FallsBackToOneStepPerLineWhenNoMarkersOrBlankLines()
    {
        const string text = """
            Quick Toast

            Ingredients
            1 slice bread

            Instructions
            Toast the bread.
            Spread butter on top.
            """;

        var recipe = Parser.Parse(text);

        Assert.Equal(2, recipe.Steps.Count);
        Assert.Equal("Toast the bread.", recipe.Steps[0].Value);
        Assert.Equal("Spread butter on top.", recipe.Steps[1].Value);
    }

    [Fact]
    public void NormalizesUnicodeFractionsInIngredientLines()
    {
        const string text = """
            Fraction Test

            Ingredients
            ½ cup sugar
            2¼ cups flour

            Instructions
            1. Mix everything together.
            """;

        var recipe = Parser.Parse(text);

        Assert.Equal("1/2 cup sugar", recipe.Ingredients[0].Value);
        Assert.Equal("2 1/4 cups flour", recipe.Ingredients[1].Value);
    }

    [Theory]
    [InlineData("Prep Time: 1 hour 30 minutes", 90)]
    [InlineData("Prep Time: 45 min", 45)]
    [InlineData("Prep Time: 2 hrs", 120)]
    [InlineData("Prep Time: 15", 15)]
    public void ParsesVariousPrepTimePhrasings(string line, int expectedMinutes)
    {
        var text = $"Title\n\n{line}\n\nIngredients\n1 cup water\n\nInstructions\n1. Do it.";

        var recipe = Parser.Parse(text);

        Assert.Equal(expectedMinutes, recipe.PrepTime);
    }

    [Fact]
    public void DefaultsToZeroAndEmptyWhenStructureIsAbsent()
    {
        const string text = "Just a title with no recognizable sections at all.";

        var recipe = Parser.Parse(text);

        Assert.Equal("Just a title with no recognizable sections at all.", recipe.Title);
        Assert.Equal(0, recipe.PrepTime);
        Assert.Equal(0, recipe.CookTime);
        Assert.Equal(0, recipe.Servings);
        Assert.Empty(recipe.Ingredients);
        Assert.Empty(recipe.Steps);
        Assert.Equal("", recipe.Description);
        Assert.Equal("", recipe.ImgUrl);
        Assert.Equal("", recipe.OriginalRecipeUrl);
    }

    [Fact]
    public void FallsBackToUntitledRecipeWhenOnlyPageNumberOrHeadersPrecedeContent()
    {
        const string text = """
            12

            Ingredients
            1 cup water

            Instructions
            1. Boil it.
            """;

        var recipe = Parser.Parse(text);

        Assert.Equal("Untitled Recipe", recipe.Title);
    }

    [Fact]
    public void ParsesRealTesseractOutputFromACookbookPhoto()
    {
        // Actual `tesseract --psm 3` stdout captured from a real photographed cookbook page — locks in
        // two behaviors found only by testing against a real photo: a title that wraps onto a second
        // line (the centered layout splits "spinach + walnut crumble" / "gnocchi" across two lines), and
        // a trailing page number ("22") after the last paragraph that must not become a spurious step.
        const string text = """
            spinach + walnut crumble

            gnocchi

            ingredients

            2 packages chestnut mushrooms
            1 package gnocchi

            1 package baby spinach
            3 garlic cloves, minced
            1/3 cup of walnuts

            1 tbsp nutrional yeast

            1 tbsp nutritional yeast

            1 tsp brown miso paste
            1 tbsp of soy sauce

            A glug of olive

            Salt and pepper to taste

            instructions

            Add walnuts, nutritional yeast and miso paste to a
            food processor and blitz until you have a crumble.
            Set aside.

            Cook gnocchi according to the package. Reserve 2
            tbsp of the cooking water.

            In the meantime add oil to the pan with the crushed
            garlic, fry for one minute or so and then add the
            mushrooms on quite a high heat until all the water
            has evaporated. Add in the soy sauce and baby
            spinach. Cook until spinach is wilted.

            Drain the gnocchi and add to pan. Mix in the walnut
            crumble and the reserved pasta water. Add black
            pepper and cheese if desired.

            22
            """;

        var recipe = Parser.Parse(text);

        Assert.Equal("spinach + walnut crumble gnocchi", recipe.Title);
        Assert.Equal(11, recipe.Ingredients.Count);
        Assert.Equal("2 packages chestnut mushrooms", recipe.Ingredients[0].Value);
        Assert.Equal(4, recipe.Steps.Count);
        Assert.Equal(
            "Add walnuts, nutritional yeast and miso paste to a food processor and blitz until you have a crumble. Set aside.",
            recipe.Steps[0].Value);
        Assert.Equal(
            "Drain the gnocchi and add to pan. Mix in the walnut crumble and the reserved pasta water. Add black pepper and cheese if desired.",
            recipe.Steps[3].Value);
        Assert.DoesNotContain(recipe.Steps, s => s.Value == "22");
    }
}
