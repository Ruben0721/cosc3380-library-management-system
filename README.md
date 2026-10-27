# Campus Library Management System

COSC 3380 team project using ASP.NET Core Razor Pages, Entity Framework Core, and MySQL.

## Repository layout

- `Library.sln`: Visual Studio solution.
- `src/Library.Web/`: C# web application (UI and backend together).
  - `Pages/`: Razor pages and their request handlers.
  - `Pages/Shared/`: shared layouts and partial views.
  - `Models/`: database entities. Existing entity names are retained to avoid unintended database mapping changes.
  - `Data/`: Entity Framework database context.
  - `Configuration/`: shared item types and statuses.
  - `Auth/`: authentication and account access rules.
  - `wwwroot/`: application CSS, JavaScript, and vendor assets.
- `frontend/`: standalone HTML homepage prototype; currently separate from the C# app.
- `database/`: database scripts (currently incomplete).
- `docs/`: project documentation.
- `screenshots/`: project screenshots.

## Run the C# application

Install the .NET 8 SDK and obtain access to the team's MySQL database.

From the repository root:

```bash
dotnet restore Library.sln
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_HOST;Port=YOUR_PORT;Database=YOUR_DATABASE;User=YOUR_USER;Password=YOUR_PASSWORD;SslMode=Required;" --project src/Library.Web/Project.csproj
dotnet build Library.sln
dotnet run --project src/Library.Web/Project.csproj --launch-profile https
```

Open the HTTPS URL printed by the application and navigate to `/Search`. The C# app currently has no root homepage. If needed, trust the local development certificate with `dotnet dev-certs https --trust`.

User secrets are loaded in Development. In hosting, set the environment variable `ConnectionStrings__DefaultConnection` instead. Never commit real credentials. The previously committed database password must be rotated; removing it from the current file does not remove it from Git history.

## Database status

`database/schema.sql` currently defines only Student. It cannot recreate the complete database expected by the application. Obtain the team's complete schema before setting up a new database; the query/report/trigger files are placeholders.

## Frontend prototype and deployment

The files under `frontend/` are a standalone prototype. Its catalog, login, and signup pages are placeholders and do not connect to Razor Pages authentication or MySQL. The C# app has those features under `/Search`, `/Account/Login`, and `/Account/Register`.

This cleanup leaves `frontend/` in place so its existing deployment path remains valid. Any C# hosting configuration or local tooling referring to `Website/Project/Project` must use `src/Library.Web` instead.

## Naming conventions

Use page-specific classes such as `InventoryModel` and `AddInventoryItemModel`. Keep entity names and database column mappings stable unless a database migration is intentionally included.
