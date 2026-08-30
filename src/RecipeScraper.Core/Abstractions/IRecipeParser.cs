namespace RecipeScraper.Core.Abstractions;

public interface IRecipeParser
{
    Recipe Parse(string html, string url);
}
