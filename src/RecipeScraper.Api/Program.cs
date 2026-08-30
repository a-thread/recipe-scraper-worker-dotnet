using Microsoft.OpenApi;
using RecipeScraper.Api.Contracts;
using RecipeScraper.Core;
using RecipeScraper.Core.UseCases;
using RecipeScraper.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRecipeScraperCore();
builder.Services.AddRecipeScraperInfrastructure();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Recipe Scraper API",
        Version = "v1",
        Description = "Scrapes structured recipe data (title, ingredients, steps, timings) out of a recipe web page.",
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Recipe Scraper API v1");
    options.RoutePrefix = "swagger";
});

app.MapGet("/", async (string? url, HttpContext context, ScrapeRecipeUseCase useCase, CancellationToken cancellationToken) =>
{
    var result = await useCase.ExecuteAsync(url, cancellationToken);

    switch (result)
    {
        case ScrapeRecipeResult.Success success:
            // Matches the original Cloudflare Worker's edge-cache TTL and open CORS policy.
            context.Response.Headers["Cache-Control"] = "s-maxage=86400";
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            return Results.Json(RecipeResponse.FromDomain(success.Recipe));
        case ScrapeRecipeResult.InvalidUrl invalid:
            return Results.Text(invalid.Reason, statusCode: StatusCodes.Status400BadRequest);
        case ScrapeRecipeResult.FetchFailed failed:
            return Results.Text(failed.Reason, statusCode: StatusCodes.Status502BadGateway);
        default:
            throw new InvalidOperationException($"Unhandled {nameof(ScrapeRecipeResult)}: {result.GetType()}");
    }
})
.WithName("ScrapeRecipe")
.WithSummary("Scrape a recipe from a web page URL")
.WithDescription("Fetches the page at ?url=, parses it for recipe data, and returns it as JSON. Rejects non-http(s) " +
    "and internal/private-network targets.")
.Produces<RecipeResponse>(StatusCodes.Status200OK, "application/json")
.Produces<string>(StatusCodes.Status400BadRequest, "text/plain")
.Produces<string>(StatusCodes.Status502BadGateway, "text/plain");

app.Run();
