# Build from the repository root.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY src/Library.Web/Project.csproj src/Library.Web/
RUN dotnet restore src/Library.Web/Project.csproj

COPY src/Library.Web/ src/Library.Web/
RUN dotnet publish src/Library.Web/Project.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
# Render terminates HTTPS at its reverse proxy.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 10000
USER app

# Render supplies PORT; use 10000 when running locally.
CMD ["sh", "-c", "exec dotnet Project.dll --urls \"http://0.0.0.0:${PORT:-10000}\""]
