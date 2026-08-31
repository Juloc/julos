ARG DEBIAN_IMAGE=debian:bookworm-20260713-slim@sha256:7b140f374b289a7c2befc338f42ebe6441b7ea838a042bbd5acbfca6ec875818
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /source
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props VERSION ./
COPY packages/JulOS.AdaptiveBrowser/runtime/JulOS.AdaptiveBrowser.Runtime/JulOS.AdaptiveBrowser.Runtime.csproj packages/JulOS.AdaptiveBrowser/runtime/JulOS.AdaptiveBrowser.Runtime/
RUN dotnet restore packages/JulOS.AdaptiveBrowser/runtime/JulOS.AdaptiveBrowser.Runtime/JulOS.AdaptiveBrowser.Runtime.csproj --runtime linux-x64
COPY packages/JulOS.AdaptiveBrowser/runtime/JulOS.AdaptiveBrowser.Runtime/ packages/JulOS.AdaptiveBrowser/runtime/JulOS.AdaptiveBrowser.Runtime/
RUN dotnet publish packages/JulOS.AdaptiveBrowser/runtime/JulOS.AdaptiveBrowser.Runtime/JulOS.AdaptiveBrowser.Runtime.csproj \
      --configuration Release \
      --runtime linux-x64 \
      --self-contained true \
      --no-restore \
      --output /application \
      -p:PublishSingleFile=true \
      -p:DebugSymbols=false \
      -p:DebugType=None

FROM ${DEBIAN_IMAGE}
ARG JULOS_VERSION=0.0.0
ARG DEBIAN_SNAPSHOT=20260720T000000Z
ARG DEBIAN_SECURITY_SNAPSHOT=20260720T000000Z
ARG CHROMIUM_VERSION=150.0.7871.124-1~deb12u1

LABEL org.opencontainers.image.title="JulOS Adaptive Browser Runtime" \
      org.opencontainers.image.description="Headless Chromium CDP screencast runtime for JulOS Adaptive Browser" \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"

ENV DEBIAN_FRONTEND=noninteractive \
    LANG=en_US.UTF-8 \
    LANGUAGE=en_US:en \
    LC_ALL=en_US.UTF-8 \
    TZ=UTC \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

RUN rm -f /etc/apt/sources.list.d/debian.sources \
    && printf '%s\n' \
        "deb [check-valid-until=no] http://snapshot.debian.org/archive/debian/${DEBIAN_SNAPSHOT}/ bookworm main" \
        "deb [check-valid-until=no] http://snapshot.debian.org/archive/debian-security/${DEBIAN_SECURITY_SNAPSHOT}/ bookworm-security main" \
        > /etc/apt/sources.list \
    && apt-get -o Acquire::Check-Valid-Until=false update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        chromium="${CHROMIUM_VERSION}" \
        fonts-dejavu-core \
        fonts-liberation \
        fonts-noto-color-emoji \
        locales \
        netcat-openbsd \
        tini \
    && sed -i 's/^# *en_US.UTF-8 UTF-8/en_US.UTF-8 UTF-8/' /etc/locale.gen \
    && locale-gen \
    && groupadd --gid 10001 julos \
    && useradd --uid 10001 --gid 10001 --create-home --home-dir /home/julos --shell /usr/sbin/nologin julos \
    && install --directory --owner=julos --group=julos --mode=0700 /tmp/julos-adaptive-browser \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /application/JulOS.AdaptiveBrowser.Runtime /opt/julos-adaptive-browser/JulOS.AdaptiveBrowser.Runtime
COPY packages/JulOS.AdaptiveBrowser/runtime/adaptive-browser-runtime.sh /usr/local/bin/adaptive-browser-runtime
RUN chmod 0555 /opt/julos-adaptive-browser/JulOS.AdaptiveBrowser.Runtime /usr/local/bin/adaptive-browser-runtime

USER 10001:10001
WORKDIR /home/julos

EXPOSE 8080/tcp
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=3 CMD ["nc", "-z", "127.0.0.1", "8080"]
ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/adaptive-browser-runtime"]
