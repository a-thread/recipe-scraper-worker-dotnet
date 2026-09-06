using RecipeScraper.Core;
using RecipeScraper.Core.Abstractions;
using RecipeScraper.Core.UseCases;

namespace RecipeScraper.Tests;

public class ImportRecipeFromImagesUseCaseTests
{
    private static Recipe MakeRecipe() => new()
    {
        Title = "Test Recipe",
        Description = "",
        ImgUrl = "",
        PrepTime = 0,
        CookTime = 0,
        Servings = 0,
        Ingredients = [],
        Steps = [],
        OriginalRecipeUrl = "",
    };

    private static RecipeImage MakeImage(int bytes = 10, string mediaType = "image/jpeg") =>
        new(new byte[bytes], mediaType);

    private sealed class FakeParser : IRecipeImageParser
    {
        public int CallCount { get; private set; }
        public Task<Recipe> ParseAsync(IReadOnlyList<RecipeImage> images, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(MakeRecipe());
        }
    }

    private sealed class ThrowingParser : IRecipeImageParser
    {
        public Task<Recipe> ParseAsync(IReadOnlyList<RecipeImage> images, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task ReturnsInvalidInputWhenNoImagesProvided()
    {
        var useCase = new ImportRecipeFromImagesUseCase(new FakeParser());

        var result = await useCase.ExecuteAsync([], CancellationToken.None);

        Assert.IsType<ImportRecipeFromImagesResult.InvalidInput>(result);
    }

    [Fact]
    public async Task ReturnsInvalidInputWhenTooManyImages()
    {
        var useCase = new ImportRecipeFromImagesUseCase(new FakeParser());
        var images = Enumerable.Range(0, ImportRecipeFromImagesUseCase.MaxImages + 1)
            .Select(_ => MakeImage()).ToList();

        var result = await useCase.ExecuteAsync(images, CancellationToken.None);

        Assert.IsType<ImportRecipeFromImagesResult.InvalidInput>(result);
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    public async Task ReturnsInvalidInputForUnsupportedMediaType(string mediaType)
    {
        var useCase = new ImportRecipeFromImagesUseCase(new FakeParser());

        var result = await useCase.ExecuteAsync([MakeImage(mediaType: mediaType)], CancellationToken.None);

        Assert.IsType<ImportRecipeFromImagesResult.InvalidInput>(result);
    }

    [Fact]
    public async Task ReturnsInvalidInputForOversizedImage()
    {
        var useCase = new ImportRecipeFromImagesUseCase(new FakeParser());

        var result = await useCase.ExecuteAsync(
            [MakeImage(ImportRecipeFromImagesUseCase.MaxImageBytes + 1)], CancellationToken.None);

        Assert.IsType<ImportRecipeFromImagesResult.InvalidInput>(result);
    }

    [Fact]
    public async Task ReturnsParseFailedWhenParserThrows()
    {
        var useCase = new ImportRecipeFromImagesUseCase(new ThrowingParser());

        var result = await useCase.ExecuteAsync([MakeImage()], CancellationToken.None);

        var failed = Assert.IsType<ImportRecipeFromImagesResult.ParseFailed>(result);
        Assert.Equal("boom", failed.Reason);
    }

    [Fact]
    public async Task ReturnsSuccessForValidImages()
    {
        var parser = new FakeParser();
        var useCase = new ImportRecipeFromImagesUseCase(parser);

        var result = await useCase.ExecuteAsync([MakeImage(), MakeImage()], CancellationToken.None);

        Assert.IsType<ImportRecipeFromImagesResult.Success>(result);
        Assert.Equal(1, parser.CallCount);
    }
}
