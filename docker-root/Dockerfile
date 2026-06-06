ARG DIFF_PDF_PLATFORM=linux/amd64

# ubuntu:jammy-20240911.1 reports Ubuntu 22.04.5 LTS.
FROM --platform=${DIFF_PDF_PLATFORM} ubuntu:jammy-20240911.1

ENV DEBIAN_FRONTEND=noninteractive

# Tools needed so add-apt-repository can import the PPA key inside a slim container
RUN apt-get update && apt-get install -y --no-install-recommends \
      software-properties-common \
      ca-certificates \
      gnupg \
      dirmngr \
      lsb-release \
  && add-apt-repository -y ppa:kicad/kicad-7.0-releases \
  && apt-get update \
  && apt-get install -y --no-install-recommends \
      libwxgtk3.2-1 \
      libwxbase3.2-1 \
      xvfb \
      xauth \
      libgtk-3-0 \
      libx11-6 \
      libxkbcommon0 \
      libxkbcommon-x11-0 \
  && rm -rf /var/lib/apt/lists/*

COPY docker-root/poppler-libs/*.deb /tmp/poppler/

RUN apt-get update && apt-get install -y \
      /tmp/poppler/libpoppler118_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/libpoppler-glib8_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/gir1.2-poppler-0.18_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/poppler-utils_22.02.0-2ubuntu0.6_amd64.deb && \
    apt-mark hold poppler-utils libpoppler118 libpoppler-glib8 gir1.2-poppler-0.18 && \
    rm -rf /var/lib/apt/lists/* /tmp/poppler

COPY job-dispatcher/docker/bin/diff-pdf-b /usr/local/bin/diff-pdf-b
RUN chmod +x /usr/local/bin/diff-pdf-b

COPY docker-root/docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh && sed -i 's/\r$//' /usr/local/bin/entrypoint.sh

WORKDIR /data
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD []
