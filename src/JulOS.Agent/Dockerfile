# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /source
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props VERSION ./
COPY src/JulOS.Contracts/JulOS.Contracts.csproj src/JulOS.Contracts/
COPY src/JulOS.Agent/JulOS.Agent.csproj src/JulOS.Agent/
RUN dotnet restore src/JulOS.Agent/JulOS.Agent.csproj
COPY src/JulOS.Contracts/ src/JulOS.Contracts/
COPY src/JulOS.Agent/ src/JulOS.Agent/
RUN dotnet publish src/JulOS.Agent/JulOS.Agent.csproj \
      --configuration Release \
      --no-restore \
      --output /application

FROM mcr.microsoft.com/dotnet/runtime:10.0.3 AS runtime
ARG JULOS_VERSION=0.0.0-development
LABEL org.opencontainers.image.title="JulOS Agent" \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"
ENV DOTNET_EnableDiagnostics=0
WORKDIR /application
COPY --from=build /application ./
USER $APP_UID
ENTRYPOINT ["dotnet", "/application/JulOS.Agent.dll"]
