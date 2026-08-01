# syntax=docker/dockerfile:1
#
# JulOS Server image. Build from the repository root:
#   docker build --file src/JulOS.Server/Dockerfile .
#
# Base image tags are pinned. Decision D020 forbids depending on a moving tag.

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build

WORKDIR /source

# Restore first from the project graph alone, so a source change does not
# invalidate the dependency layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/JulOS.Domain/JulOS.Domain.csproj src/JulOS.Domain/
COPY src/JulOS.Contracts/JulOS.Contracts.csproj src/JulOS.Contracts/
COPY src/JulOS.Application/JulOS.Application.csproj src/JulOS.Application/
COPY src/JulOS.Infrastructure/JulOS.Infrastructure.csproj src/JulOS.Infrastructure/
COPY src/JulOS.Server/JulOS.Server.csproj src/JulOS.Server/
RUN dotnet restore src/JulOS.Server/JulOS.Server.csproj

COPY src/JulOS.Domain/ src/JulOS.Domain/
COPY src/JulOS.Contracts/ src/JulOS.Contracts/
COPY src/JulOS.Application/ src/JulOS.Application/
COPY src/JulOS.Infrastructure/ src/JulOS.Infrastructure/
COPY src/JulOS.Server/ src/JulOS.Server/
RUN dotnet publish src/JulOS.Server/JulOS.Server.csproj \
      --configuration Release \
      --no-restore \
      --output /application

FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime

# The image ships no shell utilities for probing. The application answers its own
# readiness probe through --health-check, so no HTTP client tool is installed.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

WORKDIR /application

COPY --from=build /application ./

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "/application/JulOS.Server.dll"]
