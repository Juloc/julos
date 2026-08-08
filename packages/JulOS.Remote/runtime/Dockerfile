# syntax=docker/dockerfile:1

ARG GUACAMOLE_VERSION=1.6.0

# guacd is built from the official Apache Guacamole Server source release rather
# than copied from the upstream Alpine-based guacd image, because the webapp
# stage below (tomcat:9-jdk21) is Ubuntu-based (glibc) and Alpine binaries
# (musl) are not binary-compatible with it. Building on the exact same Ubuntu
# release as the final stage keeps shared-library versions aligned.
FROM ubuntu:24.04 AS guacd-build
ARG GUACAMOLE_VERSION
RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        curl \
        build-essential \
        pkg-config \
        libcairo2-dev \
        libjpeg-turbo8-dev \
        libpng-dev \
        libtool-bin \
        uuid-dev \
        freerdp2-dev \
        libpango1.0-dev \
        libssh2-1-dev \
        libtelnet-dev \
        libvncserver-dev \
        libwebsockets-dev \
        libssl-dev \
        libvorbis-dev \
        libwebp-dev \
        libpulse-dev \
    && rm -rf /var/lib/apt/lists/*
RUN curl --fail --location --silent --show-error \
        "https://archive.apache.org/dist/guacamole/${GUACAMOLE_VERSION}/source/guacamole-server-${GUACAMOLE_VERSION}.tar.gz" \
        --output /tmp/guacamole-server.tar.gz \
    && tar --extract --gzip --file /tmp/guacamole-server.tar.gz --directory /tmp \
    && cd "/tmp/guacamole-server-${GUACAMOLE_VERSION}" \
    && ./configure --with-init-dir=/tmp/discarded-init --disable-guacenc --disable-guaclog \
    && make -j"$(nproc)" \
    && make install DESTDIR=/tmp/guacd-install \
    && ldconfig

FROM guacamole/guacamole:${GUACAMOLE_VERSION} AS webapp

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS bridge-build
WORKDIR /source
COPY .editorconfig global.json Directory.Build.props Directory.Packages.props VERSION ./
COPY packages/JulOS.Remote/shared/JulOS.Remote.Transport/JulOS.Remote.Transport.csproj packages/JulOS.Remote/shared/JulOS.Remote.Transport/
COPY packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge/JulOS.Remote.ProviderBridge.csproj packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge/
RUN dotnet restore packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge/JulOS.Remote.ProviderBridge.csproj --runtime linux-x64
COPY packages/JulOS.Remote/shared/JulOS.Remote.Transport/ packages/JulOS.Remote/shared/JulOS.Remote.Transport/
COPY packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge/ packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge/
RUN dotnet publish packages/JulOS.Remote/runtime/JulOS.Remote.ProviderBridge/JulOS.Remote.ProviderBridge.csproj \
      --configuration Release \
      --runtime linux-x64 \
      --self-contained true \
      --no-restore \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      --output /application

FROM tomcat:9-jdk21
ARG JULOS_VERSION=0.0.0-development
LABEL org.opencontainers.image.title="JulOS Remote Provider Runtime" \
      org.opencontainers.image.description="Per-session Apache Guacamole provider runtime for JulOS Remote" \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        libcairo2 \
        libjpeg-turbo8 \
        libpng16-16t64 \
        libuuid1 \
        freerdp2-x11 \
        libpango-1.0-0 \
        libpangocairo-1.0-0 \
        libssh2-1 \
        libtelnet2 \
        libvncserver1 \
        libwebsockets19t64 \
        libssl3t64 \
        libvorbis0a \
        libvorbisenc2 \
        libwebp7 \
        libpulse0 \
        nginx-light \
        gettext-base \
        netcat-openbsd \
        curl \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10002 julos-remote \
    && useradd --uid 10002 --gid 10002 --create-home --home-dir /home/julos-remote --shell /usr/sbin/nologin julos-remote \
    && install --directory --owner=julos-remote --group=julos-remote --mode=0700 \
        /tmp/julos-remote-provider /var/log/nginx /var/lib/nginx /run/nginx \
    && chown -R julos-remote:julos-remote /usr/local/tomcat /etc/nginx

COPY --from=guacd-build /tmp/guacd-install/usr/local/sbin/guacd /usr/local/sbin/guacd
COPY --from=guacd-build /tmp/guacd-install/usr/local/lib/ /usr/local/lib/
COPY --from=guacd-build /tmp/guacd-install/usr/lib/x86_64-linux-gnu/freerdp2/ /usr/lib/x86_64-linux-gnu/freerdp2/
RUN ldconfig

COPY --from=webapp /opt/guacamole/ /opt/guacamole/
RUN chown -R julos-remote:julos-remote /opt/guacamole

COPY --from=bridge-build /application/ /opt/julos-remote-provider/bridge/
COPY packages/JulOS.Remote/runtime/nginx.conf.template /opt/julos-remote-provider/nginx.conf.template
COPY packages/JulOS.Remote/runtime/remote-provider-runtime.sh /usr/local/bin/remote-provider-runtime
COPY packages/JulOS.Remote/runtime/remote-provider-runtime-health.sh /usr/local/bin/remote-provider-runtime-health
RUN chmod 0555 /usr/local/bin/remote-provider-runtime /usr/local/bin/remote-provider-runtime-health \
        /opt/julos-remote-provider/bridge/JulOS.Remote.ProviderBridge \
    && chown -R julos-remote:julos-remote /opt/julos-remote-provider

ENV GUACAMOLE_HOME=/tmp/julos-remote-provider/guacamole-home \
    WEBAPP_CONTEXT=ROOT \
    JULOS_PROVIDER_LISTEN_PORT=8081

USER 10002:10002
WORKDIR /home/julos-remote

EXPOSE 8081/tcp

HEALTHCHECK --interval=10s --timeout=3s --start-period=45s --retries=3 \
    CMD ["/usr/local/bin/remote-provider-runtime-health"]

ENTRYPOINT ["/usr/local/bin/remote-provider-runtime"]
