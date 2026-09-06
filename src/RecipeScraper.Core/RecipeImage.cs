namespace RecipeScraper.Core;

/// <summary>A single photographed/scanned recipe page, handed to <see cref="Abstractions.IRecipeImageParser"/>.
/// Framework-agnostic by design — no ASP.NET <c>IFormFile</c> here; that mapping lives at the API boundary.</summary>
public sealed record RecipeImage(byte[] Data, string MediaType);
