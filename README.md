# Recipe Scraper API (.NET)

An ASP.NET Core Minimal API port of [`recipe-scraper-worker`](https://github.com/) (the Cloudflare Workers/TypeScript
version): given `GET /?url=<recipe-page>`, it fetches that page, parses it with [AngleSharp](https://anglesharp.github.io/)
into a `Recipe`, and returns JSON. The parsing heuristics (selectors, ingredient grouping, step-badge stripping,
fraction normalization) are a line-for-line port of the TypeScript/Cheerio version, so both APIs return identical
shapes for the same input.

Structured as three layers, dependencies pointing inward:

```
RecipeScraper.Api  --->  RecipeScraper.Infrastructure  --->  RecipeScraper.Core
        \_______________________________________________________/
```

- **`RecipeScraper.Core`** — the innermost layer, with no dependency on ASP.NET Core, AngleSharp, or anything
  else external. Holds the `Recipe`/`StepIngredient` entities, the `IRecipeParser`/`IRecipeHtmlFetcher`/`IRecipeCache`
  ports (interfaces), the `SsrfGuard` input-validation policy, and the `ScrapeRecipeUseCase` that orchestrates them.
- **`RecipeScraper.Infrastructure`** — implements Core's ports: `AngleSharpRecipeParser`, `HttpRecipeHtmlFetcher`
  (via `IHttpClientFactory`), and `MemoryRecipeCache` (via `IMemoryCache`).
- **`RecipeScraper.Api`** — the composition root and Presentation layer. Wires up DI, exposes the Minimal API
  endpoint, maps `ScrapeRecipeResult` to HTTP status codes, and translates the `Recipe` entity to a snake_case
  `RecipeResponse` DTO (`Contracts/`) — the JSON wire format is a presentation concern, so it's kept out of Core.
- **`test/RecipeScraper.Tests`** — xUnit tests against `Core` (the use case, with fake ports) and `Infrastructure`
  (the parser and SSRF guard) directly, no HTTP involved.

## Running locally

```
dotnet run --project src/RecipeScraper.Api
```

Then either open **http://localhost:5000/swagger** for the interactive Swagger UI, or:

```
curl -G http://localhost:5000/ --data-urlencode "url=https://example.com/some-recipe"
```

> Swagger UI is enabled unconditionally (not gated to `Development`) since this is a portfolio/demo API meant to be
> browsable wherever it's deployed. A production API handling non-public data would typically restrict it.

## Testing

```
dotnet test
```

## Deployment

A `Dockerfile` is included for container-based hosting (Azure Container Apps, Fly.io, etc.):

```
docker build -t recipe-scraper-api .
docker run -p 8080:8080 recipe-scraper-api
```

## Differences from the Cloudflare Worker version

- **Caching**: the Worker uses Cloudflare's free global edge cache; this API uses an in-process `IMemoryCache` (via
  `MemoryRecipeCache`) with the same 1-day TTL. That cache is per-instance and not shared across replicas/regions —
  swap in a Redis-backed `IRecipeCache` for a multi-instance deployment; nothing outside `Infrastructure` needs to
  change.
- **Meta-tag description extraction**: the shared parsing logic reads `<meta>` element text via `.text()`/`TextContent`,
  which is always empty for `<meta>` tags (their value lives in the `content` attribute) — a latent bug in the original
  logic. This port reads `content` for `<meta>` elements specifically, so `og:description`/`twitter:description`
  fallbacks actually work here. Worth backporting to the TypeScript versions.
