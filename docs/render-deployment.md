# Deploy to Render

This Dockerfile builds the ASP.NET Core app in src/Library.Web. It does not deploy the separate frontend prototype or change database tables.

## Render settings

After merging this change into main:

| Setting | Value |
| --- | --- |
| Service type | Web Service |
| Language | Docker |
| Branch | main |
| Root Directory | Leave blank |
| Dockerfile Path | ./Dockerfile |
| Docker Build Context | . (repository root) |
| Docker Command | Leave blank; use the image CMD |
| Instance Type | Free, for a class demo |

Set these environment variables in Render:
- ASPNETCORE_ENVIRONMENT: Production
- ConnectionStrings__DefaultConnection: the team's MySQL connection string

Connection string format (replace all placeholders privately in Render):

```text
Server=YOUR_HOST;Port=YOUR_PORT;Database=YOUR_DATABASE;User=YOUR_USER;Password=YOUR_PASSWORD;SslMode=Required;
```

The database must already contain the full application schema. No credentials belong in the Dockerfile or build arguments.

Deploy, then visit https://YOUR-SERVICE.onrender.com/Search. The application currently has no root homepage, so visiting / alone may return 404. Test registration/login and staff pages separately after deploying.

The image uses Render's PORT environment variable, falling back to 10000. Forwarded-header support is enabled for Render's reverse proxy so HTTPS redirects and secure cookies recognize the original request scheme. This image is intended to run behind a trusted proxy; reassess forwarded-header trust if hosting it elsewhere.

## Local Docker verification

From the repository root:

```bash
docker build -t campus-library .
docker run --rm -p 10000:10000 --env-file .env.render campus-library
```

Create .env.render locally with the connection-string variable above. Never commit it. HTTP-only local runs cannot fully test production secure-cookie login; validate authentication using Render's HTTPS URL.

Container restarts/redeployments can invalidate login cookies because data-protection keys are not persisted. Users may need to log in again. The free service may also sleep between visits.

## References

- https://render.com/docs/docker
- https://render.com/docs/web-services
- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0

## Inventory removal update

Apply `database/inventory-retirement.sql` once to the same MySQL database used by
Render to enable the **Remove** action in **Modify Inventory**. The website can
run on the existing schema while this update is pending. Existing rows remain
active, and staff removal records a retirement date so past loans, fines, and
other circulation records still reference the original item.
