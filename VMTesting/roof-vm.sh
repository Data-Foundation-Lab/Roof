#!/bin/bash

# Roof Build and Run Script for Unix-based systems

set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
PARENT_DIR="$(dirname "$SCRIPT_DIR")"
SRC_DIR="$PARENT_DIR/Src"

function build_compile_image() {
    local arch=$1
    # Use the parent directory (..) as the build context, matching the .bat script.
    docker build --platform linux/$arch -t roof-compile-$arch -f compile_$arch.dockerfile "$PARENT_DIR"
}

function build_runtime_image() {
    docker build --platform linux/arm64 -t roof-runtime -f runtime.dockerfile "$PARENT_DIR"
}

function compile_engine() {
    docker run --privileged --rm -it \
        -v "$SRC_DIR":/hostSrc \
        -v "$SRC_DIR/Roof.Engine/VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS \
        roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c DEBUG -r linux-arm64
}

function compile_engine_ia() {
    docker run --privileged --rm -it \
        -v "$SRC_DIR":/hostSrc \
        -v "$SRC_DIR/Roof.Engine/VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS \
        roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c IA -r linux-arm64
}

function compile_engine_beta() {
    docker run --privileged --rm -it \
        -v "$SRC_DIR":/hostSrc \
        -v "$SRC_DIR/Roof.Engine/VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS \
        roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c BETA -r linux-arm64
}

function compile_engine_release() {
    docker run --privileged --rm -it \
        -v "$SRC_DIR":/hostSrc \
        -v "$SRC_DIR/Roof.Engine/VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS \
        roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c RELEASE -r linux-arm64
}

function compile_clienttestconsole() {
    local arch=$1
    docker run --privileged --rm -it \
        -v "$SRC_DIR":/hostSrc \
        -v "$SRC_DIR/Roof.ClientTestConsole/VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS \
        roof-compile-$arch Roof.ClientTestConsole/Roof.ClientTestConsole.csproj -r linux-$arch
}

function compile_pf() {
    docker run --privileged --rm -it \
        -v "$SRC_DIR":/hostSrc \
        -v "$SRC_DIR/Roof.PathFinder/VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS \
        roof-compile-arm64 Roof.PathFinder/Roof.PathFinder.csproj -r linux-arm64
}

function run_engine() {
    # Create the docker network if it does not exist.
    docker network create --driver bridge --subnet=192.168.0.0/24 --gateway=192.168.0.1 roof-vm-net || true
    docker run --privileged --rm -it --network roof-vm-net --ip 192.168.0.253 \
        -p 8671:8671/tcp -p 22:22/tcp -p 6969:6969/udp -p 8671:8671/udp \
        -v "$SRC_DIR/Roof.Engine/VM_PUBLISHED_ARTIFACTS:/roof" \
        roof-runtime /bin/bash -c "sudo ./Roof.Engine; exec /bin/bash"
}

function run_engine_full(){
    docker network create --driver bridge --subnet=192.168.0.0/24 --gateway=192.168.0.1 roof-vm-net || true
    docker run --privileged --rm -d --name roof-runtimeVM --network roof-vm-net --ip 192.168.0.254 \
        -p 8671:8671/tcp -p 22:22/tcp -p 6969:6969/udp -p 8671:8671/udp \
        -v "$SRC_DIR/Roof.Engine/VM_PUBLISHED_ARTIFACTS:/roof" \
        roof-runtime
    docker exec -it roof-runtimeVM /bin/bash -c "sudo ./Roof.Engine; exec /bin/bash"
}

function usage() {
    echo "Usage: $0 {build-compile|build-runtime|compile-engine|compile-client|compile-pf|run-engine|run-engine-full} [arch]"
    echo "  build-compile: Requires arch (arm64 or amd64)"
    echo "  build-runtime: No arch required"
    echo "  compile-engine: No arch required (always arm64) - VERSION: DEBUG"
    echo "  compile-engine-ia: No arch required (always arm64) - VERSION: IA"
    echo "  compile-engine-beta: No arch required (always arm64) - VERSION: BETA"
    echo "  compile-engine-release: No arch required (always arm64) - VERSION: RELEASE"
    echo "  compile-clienttestconsole: Requires arch (arm64 or amd64)"
    echo "  compile-pf: No arch required"
    echo "  run-engine: No arch required"
    echo "  run-engine-full: No arch required"
    exit 1
}

function main() {
    if [ $# -lt 1 ]; then
        usage
    fi
    local action=$1
    local arch=$2

    case $action in
        build-compile)
            if [ -z "$arch" ]; then
                usage
            fi
            build_compile_image "$arch"
            ;;
        build-runtime)
            build_runtime_image
            ;;
        compile-engine)
            compile_engine
            ;;
        compile-engine-ia)
                compile_engine_ia
            ;;
        compile-engine-beta)
                compile_engine_beta
            ;;
        compile-engine-release)
                compile_engine_release
            ;;
        compile-clienttestconsole)
            if [ -z "$arch" ]; then
                usage
            fi
            compile_clienttestconsole "$arch"
            ;;
        compile-pf)
            compile_pf
            ;;
        run-engine)
            run_engine
            ;;
        run-engine-full)
            run_engine_full
            ;;
        *)
            usage
            ;;
    esac
}

main "$@"