# Use Ubuntu 22.04 (jammy) to match the Poppler .deb versions
FROM ubuntu:22.04

# Base runtime deps (note: DO NOT install libpoppler-* from the repo here)
RUN apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get install -y \
      libgtk-3-0 \
      libwxgtk3.2-dev \
      xvfb \
      x11-xkb-utils \
      xkb-data \
      ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy older poppler libs (place your .deb files under poppler-libs/ next to this Dockerfile)
# Files expected:
#   libpoppler118_22.02.0-2ubuntu0.6_amd64.deb
#   libpoppler-glib8_22.02.0-2ubuntu0.6_amd64.deb
#   gir1.2-poppler-0.18_22.02.0-2ubuntu0.6_amd64.deb
#   poppler-utils_22.02.0-2ubuntu0.6_amd64.deb
COPY poppler-libs/*.deb /tmp/poppler/

# Install the specific Poppler versions from local .deb files and hold them
RUN apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get install -y \
      /tmp/poppler/libpoppler118_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/libpoppler-glib8_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/gir1.2-poppler-0.18_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/poppler-utils_22.02.0-2ubuntu0.6_amd64.deb && \
    apt-mark hold poppler-utils libpoppler118 libpoppler-glib8 gir1.2-poppler-0.18 && \
    rm -rf /var/lib/apt/lists/* /tmp/poppler

# Copy prebuilt diff-pdf binary into the image
COPY docker/bin/diff-pdf /usr/local/bin/diff-pdf
RUN chmod +x /usr/local/bin/diff-pdf

# Copy entrypoint script and normalize line endings
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh && \
    sed -i 's/\r$//' /usr/local/bin/entrypoint.sh

# Set working directory for input and output files
WORKDIR /data

# Use entrypoint script to launch diff-pdf
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD []
