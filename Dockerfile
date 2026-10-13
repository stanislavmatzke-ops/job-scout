# Job Scout – one image for both processes (ADR-0019):
#   API (default):  web app + REST API, serves the Angular build from wwwroot
#   Worker (job):   dotnet /app/worker/JobScout.Worker.dll --contentRoot /app/worker --Scout:Mode=daily
# Build from the repository root:  docker build -t job-scout .

# ---------- 1. Angular ----------
FROM node:26-bookworm-slim AS web
WORKDIR /src/src/JobScout.Web
COPY src/JobScout.Web/package.json src/JobScout.Web/package-lock.json ./
# The same npm as the lock file was written with (package.json "packageManager") – see ci.yml
RUN npm install -g npm@$(node -p "require('./package.json').packageManager.split('@')[1]") && npm ci
COPY src/JobScout.Web/ ./
# angular.json writes the build to ../JobScout.Api/wwwroot = /src/src/JobScout.Api/wwwroot
RUN npx ng build

# ---------- 2. .NET ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
# Project files first – restore is cached until a dependency changes
COPY src/JobScout.Core/JobScout.Core.csproj src/JobScout.Core/
COPY src/JobScout.Application/JobScout.Application.csproj src/JobScout.Application/
COPY src/JobScout.Infrastructure/JobScout.Infrastructure.csproj src/JobScout.Infrastructure/
COPY src/JobScout.Migrations.Postgres/JobScout.Migrations.Postgres.csproj src/JobScout.Migrations.Postgres/
COPY src/JobScout.Api/JobScout.Api.csproj src/JobScout.Api/
COPY src/JobScout.Worker/JobScout.Worker.csproj src/JobScout.Worker/
RUN dotnet restore src/JobScout.Api/JobScout.Api.csproj && dotnet restore src/JobScout.Worker/JobScout.Worker.csproj
COPY src/ src/
COPY --from=web /src/src/JobScout.Api/wwwroot src/JobScout.Api/wwwroot
RUN dotnet publish src/JobScout.Api/JobScout.Api.csproj -c Release --no-restore -o /app/api \
 && dotnet publish src/JobScout.Worker/JobScout.Worker.csproj -c Release --no-restore -o /app/worker

# ---------- 3. Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app /app
# Headless Chromium + the system libraries it needs, installed by the Playwright driver that ships with the app
# (same version as the Microsoft.Playwright package – no separate Node.js or PowerShell in the image)
RUN /app/api/.playwright/node/linux-x64/node /app/api/.playwright/package/cli.js install --with-deps --only-shell chromium \
 && rm -rf /var/lib/apt/lists/* \
 && chmod -R a+rX /ms-playwright
WORKDIR /app/api
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobScout.Api.dll"]
