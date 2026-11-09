# Roof Engine and Client Compilation and Testing Guide

This guide outlines the existing Docker-based Linux ARM64 build workflow for Roof Engine and the local .NET 10 build workflow for Roof Client. It also covers running the Roof Engine in a testing environment using Docker.

Run these scripts only inside a disposable test VM with isolated networking. They launch privileged containers with writable host mounts. The runtime image uses public test passwords, allows root SSH login and publishes SSH and Roof ports on the Docker host. Keep production state, credentials and networks outside this environment. See [privileged development and test execution](../DEVELOPMENT.md#83-privileged-development-and-test-execution).

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Setting Up the Compilation Environment](#setting-up-the-compilation-environment)
   - [Installing Docker](#installing-docker)
   - [Compiling Roof Engine for ARM64 Linux](#compiling-roof-engine-for-arm64-linux)
   - [Compiling Roof Client on Native Platforms](#compiling-roof-client-on-native-platforms)
   - [Ancillary x64 Linux Build Limitations](#ancillary-x64-linux-build-limitations)
3. [Running Roof Engine in the Testing Environment](#running-roof-engine-in-the-testing-environment)
   - [Setting Up the Runtime Environment](#setting-up-the-runtime-environment)
   - [Testing the Roof Engine](#testing-the-roof-engine)
4. [Shutdown Instructions](#shutdown-instructions)

---

## Prerequisites

Before proceeding, ensure you have the following prerequisites installed on your system:

- **Docker**: Docker is required to build and run the Docker containers used for compilation and testing.
- **Virtual Machine or Host Environment**: The steps outlined here are intended to be run on a virtual machine or in a dedicated testing environment. Docker must support Linux ARM64 execution, natively or through configured emulation on an x64 host.

---

## Setting Up the Compilation Environment

### Installing Docker

To install Docker, follow the official installation instructions for your operating system, which can be found at https://www.docker.com/get-started/.

Verify that Docker is installed and running:

```bash
docker version
```

### Compiling Roof Engine for ARM64 Linux

1. **Navigate to the VM Testing Directory**

   ```bash
   cd VMTesting
   ```

2. **Build the Docker Image for ARM64 Compilation**

   Execute the following command to build the Docker image tailored for ARM64 Linux compilation:

   ```bash
   WINDOWS: roof-vm.bat build-compile arm64
   UNIX: ./roof-vm.sh build-compile arm64
   ```

3. **Run the Docker Container for Automatic ARM64 Compilation**

   Launch the Docker container with the following command:

   ```bash
   WINDOWS: roof-vm.bat compile-engine
   UNIX: ./roof-vm.sh compile-engine
   ```

The Engine output is `Src/Roof.Engine/VM_PUBLISHED_ARTIFACTS/`, relative to the repository root. `compile-engine-ia`, `compile-engine-beta` and `compile-engine-release` select the other configurations and share that output directory.

### Compiling Roof Client on Native Platforms

To compile Roof Client, install the .NET 10 SDK from https://dotnet.microsoft.com/en-us/download/dotnet/10.0.

After installation:

1.  **Navigate to the Roof.Client project directory.**
2.  **Build the managed Roof Client library:**

    ```bash
    dotnet build Roof.Client.csproj -c DEBUG
    ```

    Use the project file explicitly: the nested solution only declares Debug and Release, while the project also declares IA and Beta. No runtime identifier is required.

3.  **Verify that the library has been successfully built:**
    ```text
    Src/Roof.Client/bin/DEBUG/net10.0/Roof.Client.dll
    ```

    This path is relative to the repository root; `Roof.Common.dll` is alongside it. The existing project does not define a native `.so` or `.dylib` export workflow.

### Ancillary x64 Linux Build Limitations

The former `compile-client amd64` instructions were incorrect: neither wrapper dispatches `compile-client`. Use the local managed library build above.

The existing AMD64 compilation recipe and `compile-clienttestconsole amd64` action use the invalid .NET RID `linux-amd64`. Docker uses `linux/amd64`; the .NET RID is `linux-x64`. These ancillary paths require a separate script correction and verification before use.

The batch file's `:Compile-PathFinderBridge` label is not dispatched and references an absent project. It is obsolete. The existing PathFinder action is `compile-pf`, using `roof-compile-arm64` and `linux-arm64`; see [Building Roof.PathFinder](../BUILDING.md#5-building-roofpathfinder).

---

## Running Roof Engine in the Testing Environment

### Setting Up the Runtime Environment

1. **Navigate to the VM Testing Directory**

   ```bash
   cd VMTesting
   ```

2. **Build the Docker Image for the Runtime Environment**

   Execute the following command to build the Docker image for the runtime environment:

   ```bash
   WINDOWS: roof-vm.bat build-runtime
   UNIX: ./roof-vm.sh build-runtime
   ```

### Testing the Roof Engine

1. **Run the Roof Engine**

   Execute the following command to start the Roof Engine:

   ```bash
   WINDOWS: roof-vm.bat run-engine
   UNIX: ./roof-vm.sh run-engine
   ```

   To use the runtime image's default systemd command, execute the following command:

   ```bash
   WINDOWS: roof-vm.bat run-engine-full
   UNIX: ./roof-vm.sh run-engine-full
   ```

2. **Shutdown or Leave the Terminal Open**

   When finished, follow the [shutdown instructions](#shutdown-instructions) below. Ordinary `run-engine` overrides the systemd command with a shell, so systemd service operations can fail; `run-engine-full` requires working systemd/cgroup support.

   If you plan to continue using the container, you can leave the terminal open for later use.

   > **Note:** `Src/Roof.Engine/VM_PUBLISHED_ARTIFACTS/` is mounted at `/roof`. Files and state written there persist on the host. Rebuilding updates the mounted files but does not restart an already running process.

---

## Shutdown Instructions

Compilation containers exit and remove themselves when publishing finishes. For `run-engine`, stop Engine and exit the remaining Bash shell.

Exiting the `run-engine-full` interactive shell leaves its detached container running. Stop it from the host:

```bash
docker stop roof-runtimeVM
```

The wrapper uses `--rm`, so the stopped container is removed; mounted artifacts remain on the host. Docker execution was not verified during the audit because its daemon was unavailable.
