using Microsoft.Extensions.DependencyInjection;
using RecipeScraper.Core.UseCases;

namespace RecipeScraper.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddRecipeScraperCore(this IServiceCollection services)
    {
        services.AddScoped<ScrapeRecipeUseCase>();
        return services;
    }
}
