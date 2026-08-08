# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /source
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props VERSION ./
COPY src/JulOS.Contracts/JulOS.Contracts.csproj src/JulOS.Contracts/
COPY src/JulOS.RuntimeManager/JulOS.RuntimeManager.csproj src/JulOS.RuntimeManager/
RUN dotnet restore src/JulOS.RuntimeManager/JulOS.RuntimeManager.csproj
COPY src/JulOS.Contracts/ src/JulOS.Contracts/
COPY src/JulOS.RuntimeManager/ src/JulOS.RuntimeManager/
RUN dotnet publish src/JulOS.RuntimeManager/JulOS.RuntimeManager.csproj \
      --configuration Release \
      --no-restore \
      --output /application

FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime
ARG JULOS_VERSION=0.0.0-development
ARG DOCKER_CLI_VERSION=27.5.1
LABEL org.opencontainers.image.title="JulOS Runtime Manager" \
      org.opencontainers.image.description="Narrow privileged sidecar owning JulOS-owned runtime containers" \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"
ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_HTTP_PORTS=8081

# Only the static docker CLI binary is installed. Runtime Manager never receives
# the Docker daemon socket credentials of the host it runs on beyond the one
# socket mount an operator explicitly grants; it does not install or run dockerd.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl ca-certificates \
    && curl --fail --location --silent --show-error \
        "https://download.docker.com/linux/static/stable/x86_64/docker-${DOCKER_CLI_VERSION}.tgz" \
        --output /tmp/docker-cli.tgz \
    && tar --extract --gzip --file /tmp/docker-cli.tgz --directory /usr/local/bin --strip-components 1 docker/docker \
    && rm -rf /tmp/docker-cli.tgz \
    && apt-get purge --yes curl \
    && apt-get autoremove --yes \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /application
COPY --from=build /application ./

EXPOSE 8081/tcp

ENTRYPOINT ["dotnet", "/application/JulOS.RuntimeManager.dll"]
