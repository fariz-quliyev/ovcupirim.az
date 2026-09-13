# syntax=docker/dockerfile:1

# OvcuPirim.az API — production image.
#
# Build context is the `backend/` directory:
#   docker build -f backend/Dockerfile -t ovcuprim-api backend
#
# Three things this image deliberately does NOT do, matching how the application itself is written
# (see StorageSetup, HostFilteringSetup, appsettings.json's blank deployment-configuration keys):
#   - it never bakes in a real secret, hostname, or credential — every one of those is read from the
#     environment at container start, and the app refuses to start if a required one is missing;
#   - it never applies database migrations on boot — that stays an explicit, operator-run step (see
#     the `migrate` stage below and docs/deployment.md), exactly the workflow CI and local
#     development already use (`dotnet ef database update`);
#   - it never assumes more than one instance — this is still the single-instance +
#     persistent-uploads-volume architecture documented in docs/production-runbook.md.

ARG DOTNET_VERSION=10.0

# ---------------------------------------------------------------------------------------------
# Stage: restore + build. Project files are copied before the rest of the source so a source-only
# change (no new/updated package reference) reuses Docker's cached restore layer.
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-noble AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Ovcuprim.Domain/Ovcuprim.Domain.csproj src/Ovcuprim.Domain/
COPY src/Ovcuprim.Application/Ovcuprim.Application.csproj src/Ovcuprim.Application/
COPY src/Ovcuprim.Infrastructure/Ovcuprim.Infrastructure.csproj src/Ovcuprim.Infrastructure/
COPY src/Ovcuprim.Api/Ovcuprim.Api.csproj src/Ovcuprim.Api/

RUN dotnet restore src/Ovcuprim.Api/Ovcuprim.Api.csproj

COPY src/ src/

RUN dotnet publish src/Ovcuprim.Api/Ovcuprim.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------------------------
# Stage: EF Core migrations bundle — a standalone, self-contained executable that applies
# migrations against whatever connection string it is given at runtime. Built once here from the
# exact same source and `dotnet-ef` version the project pins (backend/dotnet-tools.json), so what
# gets run in production is never out of step with what CI already validates against a real
# PostgreSQL database. See the `migrate` stage below for how it is actually shipped and run.
# ---------------------------------------------------------------------------------------------
FROM build AS bundle
COPY dotnet-tools.json ./
RUN dotnet tool restore

# A self-contained, single-RID bundle needs that RID's assets in project.assets.json, which the
# `build` stage's plain `dotnet restore` (no --runtime) does not produce.
RUN dotnet restore src/Ovcuprim.Infrastructure/Ovcuprim.Infrastructure.csproj --runtime linux-x64 \
    && dotnet restore src/Ovcuprim.Api/Ovcuprim.Api.csproj --runtime linux-x64

RUN dotnet ef migrations bundle \
    --project src/Ovcuprim.Infrastructure/Ovcuprim.Infrastructure.csproj \
    --startup-project src/Ovcuprim.Api/Ovcuprim.Api.csproj \
    --configuration Release \
    --self-contained \
    --runtime linux-x64 \
    --output /out/efbundle \
    --verbose

# ---------------------------------------------------------------------------------------------
# Stage: api — the image actually run day to day. Runtime-only base image (no SDK, no build
# tools), non-root, and nothing copied into it beyond the published output.
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble AS api
WORKDIR /app

# curl is the one thing added to the runtime base: HEALTHCHECK below needs something to make an
# HTTP request with, and the container has to run as root for this single `apt-get` before it
# drops to the unprivileged built-in `app` user for everything that follows.
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

# The image ships no wwwroot/uploads content of its own — Storage__Local__RootPath must be set to
# an absolute path outside this directory (StorageSetup refuses to start otherwise), backed by the
# persistent volume mounted at deploy time. See docs/deployment.md.
#
# Pre-creating and chowning /data/uploads here, before dropping to the unprivileged user below, is
# what makes that volume writable at all: Docker seeds a brand-new named volume's contents (and
# ownership) from whatever already exists at its mount path in the image, the one time it is first
# mounted. Skip this and a fresh uploads-data volume is created owned by root, and the api
# container — which never runs as root past this point — fails StorageSetup's own write-probe at
# boot. The directory itself is just a mount point; nothing written here ever ships in the image.
RUN mkdir -p /data/uploads && chown $APP_UID:$APP_UID /data/uploads

USER $APP_UID

EXPOSE 8080

# Liveness only (matches docs/production-runbook.md §3): this must never fail because the database
# is briefly unreachable, or an orchestrator that restarts unhealthy containers would restart the
# API on every database blip instead of just waiting. Point the orchestrator's own readiness probe
# at GET /health (includes the database) separately — that endpoint answers 503 without needing a
# container restart.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/api/v1/health || exit 1

ENTRYPOINT ["dotnet", "OvcuprimApi.dll"]

# ---------------------------------------------------------------------------------------------
# Stage: migrate — a one-off, run-to-completion container. Self-contained, so it needs the OS and
# native dependencies ASP.NET Core relies on (openssl, libicu, …) but not the managed runtime
# itself — runtime-deps is the smallest base that provides those.
#
#   docker compose run --rm migrate
#
# Reads the same ConnectionStrings__Default the api service uses; see docker-compose.prod.yml.
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION}-noble AS migrate
WORKDIR /app

# Npgsql probes for GSSAPI (Kerberos) support while negotiating a connection whether or not it is
# actually used, and runtime-deps — unlike the aspnet image the api service is built on — does not
# include it. Without this, migrations still apply (Npgsql falls back cleanly), but every run logs
# a "cannot open shared object file" line that reads exactly like a failure.
RUN apt-get update \
    && apt-get install --no-install-recommends -y libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=bundle /out/efbundle ./efbundle

# No fixed connection string baked in on purpose — docker-compose.prod.yml supplies
# `--connection "$CONNECTION_STRING"` as this container's command, read from the same
# environment the api service uses. Running the image with no command prints the bundle's own
# usage rather than silently doing nothing.
ENTRYPOINT ["./efbundle"]
