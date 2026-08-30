using RecipeScraper.Core;
using RecipeScraper.Core.Abstractions;
using RecipeScraper.Core.UseCases;

namespace RecipeScraper.Tests;

public class ScrapeRecipeUseCaseTests
{
    private static Recipe MakeRecipe(string url) => new()
    {
        Title = "Test Recipe",
        Description = "",
        ImgUrl = "",
        PrepTime = 0,
        CookTime = 0,
        Servings = 0,
        Ingredients = [],
        Steps = [],
        OriginalRecipeUrl = url,
    };

    private sealed class FakeFetcher(string html) : IRecipeHtmlFetcher
    {
        public int CallCount { get; private set; }
        public Task<string> FetchHtmlAsync(Uri url, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(html);
        }
    }

    private sealed class FakeParser : IRecipeParser
    {
        public Recipe Parse(string html, string url) => MakeRecipe(url);
    }

    private sealed class ThrowingParser : IRecipeParser
    {
        public Recipe Parse(string html, string url) => throw new InvalidOperationException("boom");
    }

    private sealed class FakeCache : IRecipeCache
    {
        private readonly Dictionary<string, Recipe> _store = [];
        public Task<Recipe?> TryGetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_store.GetValueOrDefault(key));
        public Task SetAsync(string key, Recipe recipe, TimeSpan ttl, CancellationToken cancellationToken)
        {
            _store[key] = recipe;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ReturnsInvalidUrlWhenUrlIsMissing()
    {
        var useCase = new ScrapeRecipeUseCase(new FakeFetcher(""), new FakeParser(), new FakeCache());

        var result = await useCase.ExecuteAsync(null, CancellationToken.None);

        var invalid = Assert.IsType<ScrapeRecipeResult.InvalidUrl>(result);
        Assert.Equal("Missing ?url=", invalid.Reason);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("http://localhost/recipe")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task ReturnsInvalidUrlForMalformedOrBlockedTargets(string rawUrl)
    {
        var useCase = new ScrapeRecipeUseCase(new FakeFetcher(""), new FakeParser(), new FakeCache());

        var result = await useCase.ExecuteAsync(rawUrl, CancellationToken.None);

        Assert.IsType<ScrapeRecipeResult.InvalidUrl>(result);
    }

    [Fact]
    public async Task ReturnsFetchFailedWhenParsingThrows()
    {
        var useCase = new ScrapeRecipeUseCase(new FakeFetcher("<html></html>"), new ThrowingParser(), new FakeCache());

        var result = await useCase.ExecuteAsync("https://example.com/recipe", CancellationToken.None);

        var failed = Assert.IsType<ScrapeRecipeResult.FetchFailed>(result);
        Assert.Equal("boom", failed.Reason);
    }

    [Fact]
    public async Task CachesSuccessfulResultsAndSkipsRefetchingOnTheSecondCall()
    {
        var fetcher = new FakeFetcher("<html></html>");
        var useCase = new ScrapeRecipeUseCase(fetcher, new FakeParser(), new FakeCache());
        const string url = "https://example.com/recipe";

        var first = Assert.IsType<ScrapeRecipeResult.Success>(await useCase.ExecuteAsync(url, CancellationToken.None));
        var second = Assert.IsType<ScrapeRecipeResult.Success>(await useCase.ExecuteAsync(url, CancellationToken.None));

        Assert.Equal(1, fetcher.CallCount);
        Assert.Equal(first.Recipe.OriginalRecipeUrl, second.Recipe.OriginalRecipeUrl);
    }
}
