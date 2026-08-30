using RecipeScraper.Core.Abstractions;

namespace RecipeScraper.Infrastructure.Fetching;

public sealed class HttpRecipeHtmlFetcher(IHttpClientFactory httpClientFactory) : IRecipeHtmlFetcher
{
    public const string HttpClientName = "recipe-source";

    public Task<string> FetchHtmlAsync(Uri url, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        return client.GetStringAsync(url, cancellationToken);
    }
}
