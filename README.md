# Recipe Scraper Microservice

A minimal API that imports recipes from web pages, images, and PDFs.

## Features

- Scrape recipe pages with AngleSharp: `GET /?url=<recipe-page>`
- OCR photographed recipes with Tesseract: `POST /import/images`
- Import all recipes from a text-based cookbook PDF: `POST /import/pdf/bulk`
- Swagger UI, CORS, health checks, caching, and resilient outbound HTTP

The solution uses three layers:

```
Presentation -> Infrastructure -> Core
```

`Core` contains the domain model and use cases without framework dependencies. `Infrastructure`
implements parsing, HTTP, caching, OCR, and PDF extraction. `Presentation` exposes the API and maps
domain models to response DTOs. Tests cover Core and Infrastructure without HTTP calls.

## Running locally

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/RecipeScraper.Presentation
```

Open `/swagger`, or try:

```bash
curl -G http://localhost:5004/ \
  --data-urlencode "url=https://example.com/some-recipe"
```

## Image import

Accepts up to six JPEG, PNG, or WebP images, with a 10 MB limit per image. Tesseract must be on
`PATH`, or configured with `Ocr:TesseractExecutable`.

```bash
curl -X POST http://localhost:5004/import/images \
  -F "images=@page1.jpg" -F "images=@page2.jpg"
```

The result is a draft containing the detected title, ingredients, instructions, times, and servings.

## PDF import

Accepts a text-based PDF up to 20 MB and returns every recipe it finds. The parser uses
[PdfPig](https://github.com/UglyToad/PdfPig) and supports the cookbook's fixed two-column layout.
Scanned PDFs require the image endpoint instead.

```bash
curl -X POST http://localhost:5004/import/pdf/bulk \
  -F "file=@cookbook.pdf;type=application/pdf"
```

## Test and deploy

```bash
dotnet test
docker build -t recipe-scraper-api .
docker run -p 8080:8080 recipe-scraper-api
```

## Known limitations

- The cache is local to each application instance.
- Endpoints have no authentication or rate limiting.
- OCR and PDF results are first-pass drafts and should be reviewed.
