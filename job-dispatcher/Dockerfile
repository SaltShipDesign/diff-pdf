# Build from the diff-pdf repository root:
#   docker build -f job-dispatcher/Dockerfile -t diff-pdf-job-dispatcher .

ARG DIFF_PDF_PLATFORM=linux/amd64

FROM --platform=${DIFF_PDF_PLATFORM} mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
RUN apt-get update && apt-get install -y --no-install-recommends \
      clang \
      binutils \
      zlib1g-dev \
  && rm -rf /var/lib/apt/lists/*
COPY job-dispatcher/JobDispatcher.csproj job-dispatcher/
RUN dotnet restore job-dispatcher/JobDispatcher.csproj -r linux-x64
COPY job-dispatcher/ job-dispatcher/
RUN dotnet publish job-dispatcher/JobDispatcher.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishAot=true \
    -o /out

FROM --platform=${DIFF_PDF_PLATFORM} mcr.microsoft.com/dotnet/runtime-deps:8.0-jammy

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates \
      openssh-client \
      software-properties-common \
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
      x11-apps \
      libgtk-3-0 \
      libx11-6 \
      libxkbcommon0 \
      libxkbcommon-x11-0 \
  && rm -rf /var/lib/apt/lists/*

COPY docker-root/poppler-libs/*.deb /tmp/poppler/
RUN apt-get update && apt-get install -y --no-install-recommends \
      /tmp/poppler/libpoppler118_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/libpoppler-glib8_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/gir1.2-poppler-0.18_22.02.0-2ubuntu0.6_amd64.deb \
      /tmp/poppler/poppler-utils_22.02.0-2ubuntu0.6_amd64.deb \
  && apt-mark hold poppler-utils libpoppler118 libpoppler-glib8 gir1.2-poppler-0.18 \
  && rm -rf /var/lib/apt/lists/* /tmp/poppler

COPY job-dispatcher/docker/bin/diff-pdf-b /usr/local/bin/diff-pdf-b
COPY job-dispatcher/docker/diff-pdf /usr/local/bin/diff-pdf
COPY --from=build /out/job-dispatcher /usr/local/bin/job-dispatcher

RUN chmod +x /usr/local/bin/diff-pdf /usr/local/bin/diff-pdf-b /usr/local/bin/job-dispatcher \
  && mkdir -p /work

WORKDIR /work
ENTRYPOINT ["/usr/local/bin/job-dispatcher"]
