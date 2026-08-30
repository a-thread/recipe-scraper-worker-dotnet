using System.Text.Json.Serialization;
using RecipeScraper.Core;

namespace RecipeScraper.Presentation.Contracts;

public record IdTitleResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title)
{
    public static IdTitleResponse FromDomain(IdTitle item) => new(item.Id, item.Title);
}
