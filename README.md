# Campus Library Management System

COSC 3380 (Database Systems) · Team 2 · University of Houston

A web application for a campus library, built with ASP.NET Core 8 Razor Pages and MySQL 8. Students and faculty search the catalog, request and hold items, and pay fines online. Staff handle checkouts, returns, fines and maintenance. Administrators manage staff accounts and view reports.

**Live site:** <https://cosc3380-library-management-system.onrender.com/>


**Source code:** <https://github.com/Ruben0721/cosc3380-library-management-system>

Test logins for each role (Student, Faculty, Staff, Admin) are in the project document.

---

## Files in this submission

| File | What it is |
|---|---|
| `README.md` | This file |
| `dump.sql` | SQL dump of the populated database: tables, sample data, user accounts and triggers |
| `Project Document` | Types of data, user roles, triggers, queries and reports, hosted link and test logins |
| Codebase | The full repository, laid out as below |

## Repository layout

```
.
├── Library.sln
├── global.json                 .NET SDK selection
├── Dockerfile                  container build for the active web app
├── database/                   MySQL schema, triggers, and migrations
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

The application is `src/Library.Web/Project.csproj`. The `backend/` and `Website/` folders are not used by the current application.

---

## How to install

1. **Install** the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [MySQL 8.0](https://dev.mysql.com/downloads/mysql/).
2. **Create the database and import the dump:**
   ```bash
   mysql -u root -p -e "CREATE DATABASE library;"
   mysql -u root -p library < dump.sql
   ```
3. **Connect the app to the database** (run from the folder containing `Library.sln`):
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Port=3306;Database=library;User=root;Password=YOUR_MYSQL_PASSWORD;" --project src/Library.Web/Project.csproj
   ```
4. **Run the app:**
   ```bash
   dotnet run --project src/Library.Web
   ```
   Open the URL it prints and sign in with a test account from the project document.
