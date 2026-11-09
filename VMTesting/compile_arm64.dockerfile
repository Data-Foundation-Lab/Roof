FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

# Install additional dependencies
RUN apt-get update \
    && apt-get install -y \
        clang \
        llvm \
        zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_NOLOGO=true

# Set working directory for the build
WORKDIR /src

# No COPY here: the source will be provided at runtime

# ENTRYPOINT: copy source from /hostSrc, clean, then run dotnet publish.
ENTRYPOINT ["/bin/bash", "-c", "echo 'Copying source from /hostSrc to /src...'; cp -r /hostSrc/* /src/; echo 'Cleaning obj and bin directories...'; find /src -type d -name \"obj\" -exec rm -rf {} +; find /src -type d -name \"bin\" -exec rm -rf {} +; dotnet restore -r linux-arm64 && exec dotnet publish \"$@\" -o /VM_PUBLISHED_ARTIFACTS", "--"]