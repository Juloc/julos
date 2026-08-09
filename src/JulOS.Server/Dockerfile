# syntax=docker/dockerfile:1
#
# JulOS Server image. Build from the repository root:
#   docker build --file src/JulOS.Server/Dockerfile .
#
# Base image tags are pinned. Decision D020 forbids depending on a moving tag.

FROM node:24.18.0-alpine AS desktop-build

WORKDIR /desktop

COPY src/JulOS.Desktop/package.json src/JulOS.Desktop/package-lock.json ./
RUN npm ci

COPY src/JulOS.Desktop/tsconfig.json src/JulOS.Desktop/build.mjs ./
COPY src/JulOS.Desktop/src/ ./src/
COPY src/JulOS.Desktop/static/ ./static/
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build

WORKDIR /source

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

COPY --from=desktop-build /desktop/dist/ src/JulOS.Server/wwwroot/

RUN dotnet publish src/JulOS.Server/JulOS.Server.csproj \
      --configuration Release \
      --no-restore \
      --output /application

FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime

ARG JULOS_VERSION=0.0.0-development

LABEL org.opencontainers.image.title="JulOS Server" \
      org.opencontainers.image.description="JulOS control plane." \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"

RUN apt-get update \
    && apt-get install --yes --no-install-recommends gosu libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && install -d -o "$APP_UID" -g "$APP_UID" \
        /var/lib/julos/data \
        /var/lib/julos/packages \
        /var/lib/julos/data-protection

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

WORKDIR /application

COPY --from=build /application ./
# Release CI stages signed official packages here before the image build. Local
# and pull-request builds intentionally receive an empty directory instead.
COPY deploy/official-packages/ ./official-packages/
COPY src/JulOS.Server/docker-entrypoint.sh /usr/local/bin/julos-entrypoint
RUN chmod 0555 /usr/local/bin/julos-entrypoint

EXPOSE 8080

ENTRYPOINT ["/usr/local/bin/julos-entrypoint"]
CMD ["dotnet", "/application/JulOS.Server.dll"]