# https://github.com/dotnet/dotnet-docker/blob/nightly/README.sdk.md
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble-amd64 AS build

# Install additional dependencies
RUN dpkg --add-architecture amd64 \
    && apt-get update \
    && apt-get install -y \
        clang \
        gcc-x86-64-linux-gnu \
        llvm \
        zlib1g-dev \
        zlib1g-dev:amd64 \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_NOLOGO=true

# Set working directory for the build
WORKDIR /src

# No COPY here: the source will be provided at runtime

# ENTRYPOINT: copy source from /hostSrc, clean, then run dotnet publish.
ENTRYPOINT ["/bin/bash", "-c", "echo 'Copying source from /hostSrc to /src...'; cp -r /hostSrc/* /src/; echo 'Cleaning obj and bin directories...'; find /src -type d -name \"obj\" -exec rm -rf {} +; find /src -type d -name \"bin\" -exec rm -rf {} +; dotnet restore -r linux-amd64 && exec dotnet publish \"$@\" -o /VM_PUBLISHED_ARTIFACTS", "--"]