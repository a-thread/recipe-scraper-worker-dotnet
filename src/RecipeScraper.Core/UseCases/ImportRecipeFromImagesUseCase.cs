using RecipeScraper.Core.Abstractions;

namespace RecipeScraper.Core.UseCases;

public sealed class ImportRecipeFromImagesUseCase(IRecipeImageParser parser)
{
    public const int MaxImages = 6;
    public const int MaxImageBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp",
    };

    public async Task<ImportRecipeFromImagesResult> ExecuteAsync(
        IReadOnlyList<RecipeImage> images, CancellationToken cancellationToken)
    {
        if (images.Count == 0) return new ImportRecipeFromImagesResult.InvalidInput("No images provided");
        if (images.Count > MaxImages)
        {
            return new ImportRecipeFromImagesResult.InvalidInput($"Too many images (max {MaxImages})");
        }

        foreach (var image in images)
        {
            if (!AllowedMediaTypes.Contains(image.MediaType))
            {
                return new ImportRecipeFromImagesResult.InvalidInput($"Unsupported image type: {image.MediaType}");
            }
            if (image.Data.Length == 0 || image.Data.Length > MaxImageBytes)
            {
                return new ImportRecipeFromImagesResult.InvalidInput("Each image must be non-empty and under 10MB");
            }
        }

        try
        {
            var recipe = await parser.ParseAsync(images, cancellationToken);
            return new ImportRecipeFromImagesResult.Success(recipe);
        }
        catch (Exception err)
        {
            return new ImportRecipeFromImagesResult.ParseFailed(err.Message);
        }
    }
}
