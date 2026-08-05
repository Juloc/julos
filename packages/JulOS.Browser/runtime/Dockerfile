ARG DEBIAN_IMAGE=debian:bookworm-20260713-slim@sha256:7b140f374b289a7c2befc338f42ebe6441b7ea838a042bbd5acbfca6ec875818
FROM ${DEBIAN_IMAGE}

ARG JULOS_VERSION=0.0.0
ARG DEBIAN_SNAPSHOT=20260720T000000Z
ARG DEBIAN_SECURITY_SNAPSHOT=20260720T000000Z
ARG CHROMIUM_VERSION=150.0.7871.124-1~deb12u1

LABEL org.opencontainers.image.title="JulOS Browser Runtime" \
      org.opencontainers.image.description="Isolated Chromium display runtime for JulOS Browser" \
      org.opencontainers.image.source="https://github.com/Juloc/julos" \
      org.opencontainers.image.version="${JULOS_VERSION}"

ENV DEBIAN_FRONTEND=noninteractive \
    LANG=en_US.UTF-8 \
    LANGUAGE=en_US:en \
    LC_ALL=en_US.UTF-8 \
    TZ=UTC \
    DISPLAY=:99 \
    JULOS_SCREEN_GEOMETRY=1280x800x24 \
    JULOS_SCREEN_DPI=96

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
        openbox \
        tini \
        x11vnc \
        xvfb \
    && sed -i 's/^# *en_US.UTF-8 UTF-8/en_US.UTF-8 UTF-8/' /etc/locale.gen \
    && locale-gen \
    && groupadd --gid 10001 julos \
    && useradd --uid 10001 --gid 10001 --create-home --home-dir /home/julos --shell /usr/sbin/nologin julos \
    && install --directory --owner=julos --group=julos --mode=0700 /run/julos-browser /tmp/julos-browser \
    && rm -rf /var/lib/apt/lists/*

COPY packages/JulOS.Browser/runtime/browser-runtime.sh /usr/local/bin/browser-runtime
COPY packages/JulOS.Browser/runtime/browser-runtime-health.sh /usr/local/bin/browser-runtime-health

RUN chmod 0555 /usr/local/bin/browser-runtime /usr/local/bin/browser-runtime-health

USER 10001:10001
WORKDIR /home/julos

EXPOSE 5900/tcp

HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=3 \
    CMD ["/usr/local/bin/browser-runtime-health"]

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/browser-runtime"]
