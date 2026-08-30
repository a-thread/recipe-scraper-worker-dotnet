using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using RecipeScraper.Core.Abstractions;
using RecipeScraper.Infrastructure.Caching;
using RecipeScraper.Infrastructure.Fetching;
using RecipeScraper.Infrastructure.Parsing;

namespace RecipeScraper.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRecipeScraperInfrastructure(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddHttpClient(HttpRecipeHtmlFetcher.HttpClientName, client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125 Safari/537.36");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        })
        .AddStandardResilienceHandler(options =>
        {
            // Recipe sites are third-party and unreliable — retry transient failures with
            // backoff, but bound the whole attempt (including retries) to 15s.
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(16);
        });

        services.AddSingleton<IRecipeParser, AngleSharpRecipeParser>();
        services.AddSingleton<IRecipeCache, MemoryRecipeCache>();
        services.AddSingleton<IRecipeHtmlFetcher, HttpRecipeHtmlFetcher>();

        return services;
    }
}
