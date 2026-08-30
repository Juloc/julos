FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /source
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props VERSION ./
COPY packages/JulOS.AdaptiveBrowser/provider/JulOS.AdaptiveBrowser.Provider/JulOS.AdaptiveBrowser.Provider.csproj packages/JulOS.AdaptiveBrowser/provider/JulOS.AdaptiveBrowser.Provider/
RUN dotnet restore packages/JulOS.AdaptiveBrowser/provider/JulOS.AdaptiveBrowser.Provider/JulOS.AdaptiveBrowser.Provider.csproj
COPY packages/JulOS.AdaptiveBrowser/provider/JulOS.AdaptiveBrowser.Provider/ packages/JulOS.AdaptiveBrowser/provider/JulOS.AdaptiveBrowser.Provider/
RUN dotnet publish packages/JulOS.AdaptiveBrowser/provider/JulOS.AdaptiveBrowser.Provider/JulOS.AdaptiveBrowser.Provider.csproj \
      --configuration Release \
      --no-restore \
      --output /application \
      -p:UseAppHost=false \
      -p:DebugSymbols=false \
      -p:DebugType=None

FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime
ARG JULOS_VERSION=0.0.0
LABEL org.opencontainers.image.title="JulOS Adaptive Browser Provider" \
      org.opencontainers.image.description="Authenticated browser-stream WebSocket provider for JulOS" \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"
ENV ASPNETCORE_HTTP_PORTS=8081 \
    DOTNET_EnableDiagnostics=0
WORKDIR /application
COPY --from=build /application ./
USER 1654:1654
EXPOSE 8081/tcp
ENTRYPOINT ["dotnet", "/application/JulOS.AdaptiveBrowser.Provider.dll"]
