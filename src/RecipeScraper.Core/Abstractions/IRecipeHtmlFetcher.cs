namespace RecipeScraper.Core.Abstractions;

public interface IRecipeHtmlFetcher
{
    Task<string> FetchHtmlAsync(Uri url, CancellationToken cancellationToken);
}
