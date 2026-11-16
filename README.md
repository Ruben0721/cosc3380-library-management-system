# Campus Library Management System

COSC 3380 (Database Systems) · Team 2 · University of Houston

A web application for running a campus library. Students and faculty search the catalog, request items for pickup, place holds, keep a reading list, return books through a book drop, and pay fines online. Staff handle checkouts, returns, pickups, fines, maintenance and room setup. Administrators also manage staff accounts and view analytical reports.

Built with **ASP.NET Core 8 Razor Pages**, **Entity Framework Core** and **MySQL 8** hosted on **Aiven**, deployed to **Render** with Docker. Library business rules are enforced in the database with triggers.

**Live site:** <https://cosc3380-library-management-system.onrender.com/>


Test logins for each role (Student, Faculty, Staff, Admin) are in the project document.

---

## What's in this submission

| File / folder | What it is |
|---|---|
| `README.md` | This file: project description, file guide and installation steps |
| `library_dump_clean.sql` | **SQL dump of the populated database**: all tables, sample data, user accounts and triggers. Use this to install. |
| `Project Document` | Types of data, user roles, semantic constraints and triggers, queries and reports, hosted link and test logins |
| `Library.sln` | .NET solution file |
| `src/Library.Web/` | Complete source code of the web application |
| `database/schema.sql` | Table definitions for an empty database |
| `database/triggers.sql` | Source code of all business-rule triggers |
| `database/migrations/` | Dated scripts for changes made after the initial schema |
| `Dockerfile` | Container build used for hosting on Render |
| `global.json` | Pins the .NET 8 SDK |

---

## Installing the project from the submission

1. **Install** the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [MySQL 8.0](https://dev.mysql.com/downloads/mysql/).
2. **Create the database and import the dump** (tables, sample data, accounts and triggers):
   ```bash
   mysql -u root -p -e "CREATE DATABASE library CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;"
   mysql -u root -p library < library_dump_clean.sql
   ```
3. **Set the connection string** (from the folder containing `Library.sln`):
   ```bash
   dotnet restore Library.sln
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Port=3306;Database=library;User=root;Password=YOUR_MYSQL_PASSWORD;" --project src/Library.Web/Project.csproj
   ```
4. **Run** the app and open the URL it prints:
   ```bash
   dotnet run --project src/Library.Web
   ```
5. **Sign in** with any test account from the project document.

### Troubleshooting
- **"A compatible .NET SDK was not found"**: install the .NET 8 SDK (step 1).
- **"Unable to connect to any of the specified MySQL hosts"**: check the server, port, user and password in the connection string, and that MySQL is running.
- **"Table 'library.Libraryuser' doesn't exist"**: the dump wasn't imported into the database named in the connection string. Table names are case-sensitive (`Libraryuser`, `Rentals`, `Hold`).

---

## Features

### Students and faculty
- **Catalog**: search by title, author or barcode, browse by category, see covers and current status.
- **Request for pickup**: request an available item; it is held at the circulation desk for 2 days and the loan starts at pickup.
- **Holds**: when every copy is out, join a first-come-first-served waiting list; the next returned copy is set aside automatically.
- **Reading list**: save titles and see each one's availability.
- **Book drop**: mark a book as dropped off; its late fee stops from that moment until staff check it in.
- **Fines**: see charged fines and late fees building up on overdue items, and pay online.
- **Sign-in notice**: a pop-up shows items ready for pickup and unpaid fines.
- **Rooms**: view room availability and reserve study rooms.
- **My Library account**: checkouts, requests and holds, reading list, fines and personal info.

### Staff
- Checkout, returns (with condition: Good, Damaged or Lost) and pickup hand-out.
- Book drop check-in, dated at the drop-off time.
- Fines and payments, plus a fine and payment history lookup by member name.
- Maintenance requests and routine inspections.
- Room setup tasks.
- Inventory: add and edit items, upload book covers.
- Managing student and faculty accounts (create logins, reset passwords, view a member's rentals).

### Administrators
Everything staff can do, plus managing staff and admin accounts and the three reports below.

### Reports (admin only)
Each report has date-range and category filters, comparisons with the previous period, written findings and recommendations, and detailed tables. **Every number links to the records it was counted from**, with CSV download.

- **Circulation & Collection Performance**: checkouts, unique borrowers, utilization, monthly checkouts by category, and the most in-demand, most waitlisted, least borrowed and never borrowed titles.
- **Overdue Returns & Fines**: late-return rate, average days late, fines outstanding and collection rate, late returns by category, monthly trends, repeat late returners and payments by method.
- **Equipment Maintenance**: share of items in service, open requests, average days to resolve, damage rate, open repair queue, repair-or-replace recommendations and monthly activity.

A public **Borrowing & fines policy** page (`/Policies`) lists loan periods and fines per category, read from the `FinePolicy` table.

---

## Business rules enforced by triggers

| Rule | Trigger(s) |
|---|---|
| Members owing more than $10 (unpaid fines + late fees building up) can't borrow | `trg_block_rental_unpaid_fines` **(main business rule)** |
| Late, damaged and lost returns are fined from the category's fine policy (daily rate up to a cap, damage fee, replacement cost) | `trg_rental_return_fines` **(main business rule)** |
| Members can't exceed their rental and hold limits | `ValidateRentalLimit`, hold-limit trigger on `Hold` |
| Only Available items can be checked out, with valid dates | `ValidateRentalBeforeInsert` |
| Damaged returns open a repair request; 3 repairs in 180 days open a replacement review; routine inspections open every N returns | `trg_rental_return_maintenance` |
| Resolving maintenance puts the item back in service | `trg_maintenance_resolved` |
| A returned copy goes to the member who has waited longest, with a 2-day pickup deadline | `trg_rental_return_hold` |
| Room bookings must fit capacity and not overlap | `ValidateRoomReservation` |
| Confirmed bookings create a staff setup task; cancelling cancels it | `trg_room_reservation_setup`, `trg_room_reservation_cancel_setup` |

The full trigger code is in `database/triggers.sql`.

---

## Repository layout

```
Library.sln
src/Library.Web/          ASP.NET Core web app (UI and backend)
  Pages/                  Razor pages and handlers
  Pages/Staff/            staff-only pages (protected as a folder)
  Pages/Shared/           layout and partial views
  Models/                 entity classes
  Data/                   LibraryDbContext
  Configuration/          item types and statuses
  Auth/                   authentication and account access rules
  wwwroot/                CSS, JavaScript, images
database/
  schema.sql              tables
  triggers.sql            triggers
  migrations/             later changes, applied in date order
docs/, screenshots/
```

---

## Deployment (Render)

- Built from the repository's `Dockerfile`.
- Environment variables:
  - `ConnectionStrings__DefaultConnection`: the MySQL connection string
  - `TZ=America/Chicago`: so due dates, deadlines and greetings use local time
