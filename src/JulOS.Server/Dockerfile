# syntax=docker/dockerfile:1
#
# JulOS Server image. Build from the repository root:
#   docker build --file src/JulOS.Server/Dockerfile .
#
# Base image tags are pinned. Decision D020 forbids depending on a moving tag.

FROM node:24.18.0-alpine AS desktop-build

WORKDIR /desktop

# Dependency installation is isolated from source changes. The production build
# emits native ES modules and static files without retaining Node in the runtime.
COPY src/JulOS.Desktop/package.json src/JulOS.Desktop/package-lock.json ./
RUN npm ci

COPY src/JulOS.Desktop/tsconfig.json src/JulOS.Desktop/build.mjs ./
COPY src/JulOS.Desktop/src/ ./src/
COPY src/JulOS.Desktop/static/ ./static/
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build

WORKDIR /source

# Restore first from the project graph alone, so a source change does not
# invalidate the dependency layer. The analyzer configuration is copied as part
# of the build contract so container publishing enforces the same rules as CI.
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props VERSION ./
COPY src/JulOS.Domain/JulOS.Domain.csproj src/JulOS.Domain/
COPY src/JulOS.Contracts/JulOS.Contracts.csproj src/JulOS.Contracts/
COPY src/JulOS.Application/JulOS.Application.csproj src/JulOS.Application/
COPY src/JulOS.PackageSdk/JulOS.PackageSdk.csproj src/JulOS.PackageSdk/
COPY src/JulOS.Infrastructure/JulOS.Infrastructure.csproj src/JulOS.Infrastructure/
COPY src/JulOS.Server/JulOS.Server.csproj src/JulOS.Server/
RUN dotnet restore src/JulOS.Server/JulOS.Server.csproj

COPY src/JulOS.Domain/ src/JulOS.Domain/
COPY src/JulOS.Contracts/ src/JulOS.Contracts/
COPY src/JulOS.Application/ src/JulOS.Application/
COPY src/JulOS.PackageSdk/ src/JulOS.PackageSdk/
COPY src/JulOS.Infrastructure/ src/JulOS.Infrastructure/
COPY src/JulOS.Server/ src/JulOS.Server/
RUN dotnet publish src/JulOS.Server/JulOS.Server.csproj \
      --configuration Release \
      --no-restore \
      --output /application

FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime

# The application version always comes from the VERSION file above. This argument
# only labels the image, because a Dockerfile cannot read a file into a label.
# tools/validate.mjs and continuous integration pass the real value; a plain
# `docker compose up --build` produces a clearly marked development image.
ARG JULOS_VERSION=0.0.0-development

LABEL org.opencontainers.image.title="JulOS Server" \
      org.opencontainers.image.description="JulOS control plane." \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"

# Npgsql loads the system GSSAPI library even when password authentication is
# used. gosu is used only by the entrypoint to repair ownership of JulOS-owned
# paths in pre-existing named volumes before dropping to the runtime UID.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends gosu libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && install -d -o "$APP_UID" -g "$APP_UID" \
        /var/lib/julos/data \
        /var/lib/julos/packages \
        /var/lib/julos/data-protection

# The image ships no HTTP client for probing. The application answers its own
# readiness probe through --health-check.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

WORKDIR /application

COPY --from=build /application ./
COPY --from=desktop-build /desktop/dist ./wwwroot/
COPY src/JulOS.Server/docker-entrypoint.sh /usr/local/bin/julos-entrypoint
RUN chmod 0555 /usr/local/bin/julos-entrypoint

EXPOSE 8080

# The entrypoint starts as root only long enough to repair JulOS volume ownership,
# then execs the server as APP_UID. The server process itself is never root.
ENTRYPOINT ["/usr/local/bin/julos-entrypoint"]
CMD ["dotnet", "/application/JulOS.Server.dll"]
