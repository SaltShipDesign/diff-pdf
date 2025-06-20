# Use the latest Ubuntu base image
FROM ubuntu:latest

# Install runtime dependencies (GTK+, Poppler, wxWidgets, Xvfb for headless rendering, and X keyboard data)
RUN apt-get update && \
    DEBIAN_FRONTEND=noninteractive apt-get install -y \
      libgtk-3-0 \
      libpoppler-glib8 \
      libwxgtk3.2-dev \
      xvfb \
      x11-xkb-utils \
      xkb-data \
    && rm -rf /var/lib/apt/lists/*

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
# Allow passing PDF filenames and optional output name as arguments
CMD []