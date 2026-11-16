# Campus Library Management System

COSC 3380 team project using ASP.NET Core Razor Pages, Entity Framework Core, and MySQL.

## Repository layout

```text
.
├── Library.sln
├── global.json                 .NET SDK selection
├── Dockerfile                  container build for the active web app
├── database/                   MySQL schema, queries, reports, triggers, and migrations
├── docs/                       deployment and project documentation
├── screenshots/                project screenshots
├── src/
│   └── Library.Web/            active ASP.NET Core Razor Pages application
│       ├── Auth/               authentication and account access rules
│       ├── Configuration/      application options and shared configuration
│       ├── Data/               Entity Framework Core database context
│       ├── Models/             database entities and domain models
│       ├── Pages/              Razor pages, page models, layouts, and partials
│       ├── Properties/         local launch settings
│       ├── Services/           hosted and application services
│       └── wwwroot/            CSS, JavaScript, images, and vendor assets
├── tests/
│   └── Library.AuthChecks/     standalone authentication and validation checks
├── backend/                    legacy backend files retained for reference
└── Website/                    legacy website files retained for reference
```

The active solution contains `src/Library.Web/Project.csproj`. The `backend/` and
`Website/` directories are not used by the current application; use the project
under `src/Library.Web/` for development and deployment.

## Run the C# application

Install the .NET 8 SDK and obtain access to the team's MySQL database.

From the repository root:

```bash
dotnet restore Library.sln
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_HOST;Port=YOUR_PORT;Database=YOUR_DATABASE;User=YOUR_USER;Password=YOUR_PASSWORD;SslMode=Required;" --project src/Library.Web/Project.csproj
dotnet build Library.sln
dotnet run --project src/Library.Web/Project.csproj --launch-profile https
```

Open the HTTPS URL printed by the application. If needed, trust the local development certificate with `dotnet dev-certs https --trust`.

User secrets are loaded in Development. In hosting, set the environment variable `ConnectionStrings__DefaultConnection` instead. Never commit real credentials. The previously committed database password must be rotated; removing it from the current file does not remove it from Git history.

## Database status

`database/schema.sql` contains the table definitions required by the application.
Additional SQL files contain queries, reports, triggers, and migrations. Apply the
base schema to a new MySQL database, then apply the relevant migration scripts in
`database/migrations/` once. The application connects to MySQL through
`LibraryDbContext`.

## Deployment

The root `Dockerfile` builds and publishes `src/Library.Web/Project.csproj` for
container hosting. Any C# hosting configuration or local tooling referring to
`Website/Project/Project` must use `src/Library.Web` instead.

## Naming conventions

Use page-specific classes such as `InventoryModel` and `AddInventoryItemModel`. Keep entity names and database column mappings stable unless a database migration is intentionally included.


## Book covers and awaiting-pickup status (November 2026)

Before deploying the updated ASP.NET Razor application, apply `database/migrations/2026-11-15-book-covers.sql` **once** to the MySQL/Aiven database. This adds nullable `CoverImage` (MEDIUMBLOB) and `CoverImageContentType` columns to `Books`. Existing records remain without a cover until staff upload one.

Staff can add, replace, and remove JPEG, PNG, or WebP covers (maximum 2 MB) using Add Inventory Item / Edit Item. Covers are stored in MySQL, not in local wwwroot files. The public catalog and item details display them.

A Ready hold is shown as **Awaiting Pickup** in catalog, item details, and staff inventory, including the inventory status filter. The underlying Inventory.Status remains Available while the hold is Ready so that existing database rules and hold-release workflows continue to work. Borrow requests lock the physical copy and reject an existing Ready hold; regular checkout rejects copies reserved for another member. Once pickup is fulfilled, the loan changes inventory status to Checked Out. Cancellation and expiration release the Ready hold; queued holds are promoted by the existing circulation logic.
