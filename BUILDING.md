# Building Roof from Source

## Overview

Roof is an open-source project developed by Data Foundation Lab (DFLab) towards a Personal Internet.

This document describes the current build procedures for Roof's software components, including NativeAOT compilation, local library development and rebuilding applications with modified LGPL-covered libraries.

Roof is developed using C# and .NET 10.

The initial open-source release preserves Roof's existing architecture and deployment model, with Roof.Engine primarily designed to operate on Raspberry Pi 5 hardware running Linux ARM64.

Before running Roof.Engine or the VMTesting scripts, review the [development configuration and execution limits](DEVELOPMENT.md#81-build-configurations-and-hosted-services). Even DEBUG can change Linux services, accounts and networking on the machine where it runs.

Hardware-independent deployment and broader platform compatibility are near-future development objectives. They are not supported release guarantees.

This document describes the current build workflows rather than anticipated near-future and future capabilities.

## 1. Repository Structure

The initial public repository is expected to include:

```text
Roof/
├── Src/
│   ├── Roof.Engine/
│   ├── Roof.PathFinder/
│   ├── Roof.Client/
│   └── Roof.Common/
|   └── Roof.ClientTestConsole/
├── VMTesting/
│   ├── roof-vm.sh
│   ├── roof-vm.bat
│   ├── compile_arm64.dockerfile
│   └── compile_amd64.dockerfile
|   └── runtime.dockerfile
|   └── entrypoint.sh
├── LICENSES/
├── README.md
├── LICENSING.md
├── BUILDING.md
└── DISTRIBUTION.md
```

The directory listing shows the principal components and build utilities relevant to this document. Other files and supported utilities may be present in the published repository.

The publication scope of additional development projects is determined separately.

## 2. Build Overview

Roof currently uses two primary build approaches.

| Component              | Framework | Build method                       |
| ---------------------- | --------- | ---------------------------------- |
| Roof.Engine            | net10.0   | Docker-based NativeAOT compilation |
| Roof.PathFinder        | net10.0   | Docker-based NativeAOT compilation |
| Roof.Client            | net10.0   | Local .NET SDK build               |
| Roof.Common            | net10.0   | Local .NET SDK build               |
| Roof.ClientTestConsole | net10.0   | IDE development and debugging      |

### 2.1 Build Configurations

The project uses the following named configurations:

- `DEBUG` - Debugging
- `IA` - Internal Availability
- `BETA` - Beta Testing
- `RELEASE` - Release

These configurations may represent different development or distribution settings.

They must not be assumed to be interchangeable. Developers should use the configuration appropriate to the intended build and refer to the corresponding project settings.

### 2.2 Target Platforms

The initial release focuses on the existing supported implementation.

Roof.Engine is built for Linux ARM64, targeting Raspberry Pi 5 deployment.

The supplied Roof.PathFinder build scripts also target Linux ARM64. The ancillary AMD64 recipe currently uses the invalid .NET RID `linux-amd64`; it is not a working x64 build path unchanged.

The reusable Roof.Client and Roof.Common libraries target .NET 10 and can be built using the installed .NET 10 SDK on an appropriate development environment.

Support for additional operating systems and hardware platforms may be introduced in future releases.

## 3. Prerequisites

### 3.1 .NET 10

Install the .NET 10 SDK for local library builds and development.

https://dotnet.microsoft.com/download/dotnet/10.0

Verify the installation:

```bash
dotnet --info
```

The SDK is required for local builds of Roof.Client and Roof.Common.

The NativeAOT workflows described below use the .NET SDK inside Docker images.

The exact SDK and dependency versions used for an official binary release should be recorded with its source and build information.

### 3.2 Docker

The current NativeAOT workflow requires Docker with support for the relevant Linux architecture.

The build process uses:

```text
mcr.microsoft.com/dotnet/sdk:10.0-noble
```

The ARM64 compilation environment installs:

- Clang
- LLVM
- zlib development files

Docker must be configured to run Linux ARM64 containers. Depending on the host architecture, this may require appropriate emulation or a compatible native build environment.

The current build scripts use privileged containers.

Developers should review this requirement before execution and run the build workflow only in an appropriately trusted environment.

### 3.3 Development Environment

A compatible IDE may be used for local development and debugging.

Roof.ClientTestConsole is currently developed and executed through an IDE using the .NET runtime rather than the ordinary production NativeAOT deployment workflow.

## 4. Building Roof.Engine

Roof.Engine is the node-side application intended for Raspberry Pi 5 hardware.

Its project targets `net10.0`, and the current distribution build uses Linux ARM64 NativeAOT compilation.

Because of its native build dependencies, the established workflow uses the Docker build environment under `VMTesting/` instead of requiring developers to install the Linux ARM64 native compilation toolchain directly on their host system.

### 4.1 Building the ARM64 Compilation Image

The build image is defined in:

```text
VMTesting/compile_arm64.dockerfile
```

The Dockerfile uses the .NET 10 SDK with Ubuntu Noble and installs the native compilation dependencies.

From the `VMTesting/` directory:
**Linux/macOS:**

```bash
./roof-vm.sh build-compile arm64
```

**Windows:**

```bat
roof-vm.bat build-compile arm64
```

This creates the Docker image:

```text
roof-compile-arm64
```

On Unix-based systems, the script may need executable permission:

```bash
chmod +x roof-vm.sh
```

### 4.2 Compiling Roof.Engine

The `roof-vm` scripts provide dedicated commands for the existing build configurations.

| Configuration | Command                  |
| ------------- | ------------------------ |
| DEBUG         | `compile-engine`         |
| IA            | `compile-engine-ia`      |
| BETA          | `compile-engine-beta`    |
| RELEASE       | `compile-engine-release` |

For example, from `VMTesting/`:
**Linux/macOS:**

```bash
./roof-vm.sh compile-engine
```

**Windows:**

```bat
roof-vm.bat compile-engine
```

To compile the RELEASE configuration:
**Linux/macOS:**

```bash
./roof-vm.sh compile-engine-release
```

**Windows:**

```bat
roof-vm.bat compile-engine-release
```

The wrapper invokes Docker using the corresponding project file, configuration and runtime identifier.

For example, the RELEASE action passes:

```text
Roof.Engine/Roof.Engine.csproj
-c RELEASE
-r linux-arm64
```

The compilation process is configured to publish the native output into:

```text
Src/Roof.Engine/VM_PUBLISHED_ARTIFACTS/
```

### 4.3 Compilation Process

The compilation container:

1. Receives the repository's `Src/` directory through a bind mount.
2. Copies the source files into the container's `/src` directory.
3. Removes existing `bin` and `obj` directories from the copied source.
4. Restores the required dependencies.
5. Invokes `dotnet publish` using the supplied project, configuration and runtime arguments.
6. Writes the published output to the mounted artifact directory.

The build process copies the source into the container rather than modifying the host source directories directly as part of its normal compilation procedure.

The source layout and dependency restoration procedure must be verified against the final published repository.

### 4.4 Running Roof.Engine in VMTesting

The existing VMTesting scripts include a Docker-based runtime environment.

A runtime image can be built using:

```bash
./roof-vm.sh build-runtime
```

The available runtime actions include:

```bash
./roof-vm.sh run-engine
```

and:

```bash
./roof-vm.sh run-engine-full
```

The corresponding commands are available through `roof-vm.bat` on Windows.

These commands use the runtime container and networking configuration defined by the VMTesting scripts.

The runtime workflow includes privileged container execution and predefined network settings and port mappings.

**Important:** The VMTesting runtime is a development and testing facility. It is not a substitute for validating Roof.Engine on its intended Raspberry Pi 5 hardware.

The runtime image is defined by the included `VMTesting/runtime.dockerfile`. See [VMTesting/readme.md](VMTesting/readme.md) for runtime modes, artifact mounts and shutdown commands.

## 5. Building Roof.PathFinder

Roof.PathFinder is the relay application used for connectivity between Roof participants.

The current build process follows the same Docker-based NativeAOT approach as Roof.Engine.

Both supplied wrappers use `roof-compile-arm64` and target:

```text
linux-arm64
```

### 5.1 Preparing the Build Environment

From `VMTesting/`, build the ARM64 image if it has not already been created:

```bash
./roof-vm.sh build-compile arm64
```

For Windows:

```bat
roof-vm.bat build-compile arm64
```

### 5.2 Compiling Roof.PathFinder

From the `VMTesting/` directory:

**Linux/macOS:**

```bash
./roof-vm.sh compile-pf
```

**Windows:**

```bat
roof-vm.bat compile-pf
```

The current wrapper passes:

```text
Roof.PathFinder/Roof.PathFinder.csproj
-r linux-arm64
```

Unlike Roof.Engine's dedicated actions, the supplied PathFinder command does not explicitly select a named build configuration.

With the .NET 10 SDK and this `net10.0` project, `dotnet publish` defaults to `Release`. The wrapper itself does not pass `-c`.

Published output is directed to:

```text
Src/Roof.PathFinder/VM_PUBLISHED_ARTIFACTS/
```

### 5.3 Deployment

The compilation workflow produces the application artifact.

Deployment to an operational relay environment requires separate configuration and installation steps.

The applicable deployment and source-availability procedures will be documented in `DISTRIBUTION.md`.

## 6. Building Roof.Common

Roof.Common contains shared functionality used by other Roof components.

The project targets:

```text
net10.0
```

It is licensed under LGPL-3.0-only.

Roof.Common can be built locally using the installed .NET 10 SDK.

Navigate to:

```text
Src/Roof.Common/
```

Run:

```bash
dotnet build -c DEBUG
```

Alternatively:

```bash
dotnet build -c IA
dotnet build -c BETA
dotnet build -c RELEASE
```

The relevant project configuration determines the build outputs and settings.

No Docker-based NativeAOT compilation environment is required for the normal local Roof.Common library build.

## 7. Building Roof.Client

Roof.Client provides reusable client functionality for communicating with Roof.

The project targets:

```text
net10.0
```

It is licensed under LGPL-3.0-only and references Roof.Common.

Navigate to:

```text
Src/Roof.Client/
```

Run:

```bash
dotnet build Roof.Client.csproj -c DEBUG
```

Other supported build configurations are:

```bash
dotnet build Roof.Client.csproj -c IA
dotnet build Roof.Client.csproj -c BETA
dotnet build Roof.Client.csproj -c RELEASE
```

Specify the project file explicitly: the nested `Roof.Client.sln` declares only Debug and Release, while the project also declares IA and Beta.

The local .NET SDK builds Roof.Client and its applicable project dependencies. Output is the managed `bin/<Configuration>/net10.0/Roof.Client.dll`, with `Roof.Common.dll` alongside it. These commands do not produce a native `.so` or `.dylib`.

Roof.Client is also consumed by other applications, including RoofDeck.

The separate RoofDeck repository documents how its desktop application incorporates the Roof.Client and Roof.Common libraries.

## 8. Roof.ClientTestConsole

Roof.ClientTestConsole is an internal development and testing application targeting `net10.0`.

Its established development workflow uses an IDE to build, execute and debug the application using the .NET runtime.

From `VMTesting/`, after building `roof-compile-arm64`, the scripts also provide:

```bash
./roof-vm.sh compile-clienttestconsole arm64
```

On Windows, use `roof-vm.bat compile-clienttestconsole arm64`. Output is `Src/Roof.ClientTestConsole/VM_PUBLISHED_ARTIFACTS/`.

However, the presence of this script does not establish that the Docker build path is the primary supported test-console workflow.

## 9. LGPLv3 Library Modification and Rebuilding

### 9.1 Licensing Context

Roof.Common and Roof.Client are licensed under LGPL-3.0-only.

Roof.Engine incorporates Roof.Common through a project reference.

Roof.PathFinder incorporates Roof.Common through a project reference.

These applications use NativeAOT compilation, which can incorporate retained library code directly into the resulting native executable.

DFLab's selected compliance approach is the source-based recombination mechanism described in LGPLv3 section 4(d)(0).

Under this approach, users must be provided with the applicable library and application code in a suitable form for recombining or rebuilding the application using modified LGPL-covered libraries.

The existing NativeAOT compilation model does not require a separately replaceable runtime DLL when the source-based recombination requirements are satisfied.

### 9.2 Modifying Roof.Common

A developer may modify Roof.Common in their local source checkout, subject to the applicable LGPLv3 terms.

Because Roof.Engine and Roof.PathFinder reference Roof.Common through project references, their compilation workflows are intended to incorporate the currently referenced Roof.Common source.

The developer can then use the existing Docker build system to rebuild the affected application.

For Roof.Engine:

```bash
./roof-vm.sh compile-engine-release
```

For Roof.PathFinder:

```bash
./roof-vm.sh compile-pf
```

Equivalent actions are available through the Windows batch script.

The resulting native application must be tested to verify that the modified Roof.Common behaviour is incorporated and executable.

### 9.3 Modifying Roof.Client

Roof.Client can be modified and rebuilt using the local .NET 10 SDK.

From `Src/Roof.Client/`:

```bash
dotnet build Roof.Client.csproj -c RELEASE
```

Applications consuming Roof.Client may require further rebuilding to incorporate those modifications.

The procedure for rebuilding RoofDeck with modified Roof.Client or Roof.Common source will be maintained in the RoofDeck repository.

### 9.4 Required Recombination Verification

The source-based recombination procedure must be tested against the published release configuration.

For each relevant NativeAOT application:

1. Obtain the corresponding public source revision.
2. Install or prepare the documented build environment.
3. Modify an observable function in the applicable LGPL-covered library.
4. Rebuild the application using the existing project references and build workflow.
5. Confirm that the resulting executable contains the modified behaviour.
6. Install or execute the rebuilt application in a suitable environment.
7. Record the results.

Build success alone does not prove successful recombination.

The modified behaviour must be demonstrated in the resulting application.

The initial release's LGPLv3 compliance verification should use the actual ARM64 build workflow rather than an unrelated simplified example.

## 10. Installation on Raspberry Pi 5

Roof.Engine's current intended hardware target is Raspberry Pi 5.

The open-source release does not initially guarantee operation on other single-board computers or arbitrary Linux systems.

The actual installation procedure may depend on:

- The supported Linux distribution and version.
- Required operating-system packages and services.
- Filesystem layout.
- Network-interface configuration.
- Required permissions.
- Roof.Engine configuration.
- Device-specific dependencies.
- Service startup and shutdown procedures.

These requirements must be documented against the release's actual supported Raspberry Pi 5 environment.

### Modified Application Installation

Where the applicable GPLv3/LGPLv3 requirements mandate Installation Information, the distribution must provide the information necessary to install and execute a modified application to the extent required by those licenses.

For Roof.Engine, this may involve replacing the executable on an existing installation or provisioning a suitable Raspberry Pi environment with the modified build.

The installation process must not depend on undisclosed private authorisation mechanisms where the applicable license requires that information to be provided.

Detailed procedures and distribution-specific obligations will be maintained in `DISTRIBUTION.md`.

## 11. VMTesting Build System

VMTesting contains the existing build and virtualised testing infrastructure.

The principal supplied components include:
| File | Purpose |
|---|---|
| `roof-vm.sh` | Unix-based build and runtime wrapper |
| `roof-vm.bat` | Windows build and runtime wrapper |
| `compile_arm64.dockerfile` | Linux ARM64 build environment |
| `compile_amd64.dockerfile` | Additional AMD64 build environment |

The ARM64 image is used by the existing Roof.Engine and Roof.PathFinder compilation workflows.

The AMD64 recipe currently restores with `linux-amd64`, and `compile-clienttestconsole amd64` also passes that invalid .NET RID. Docker uses `linux/amd64`; the .NET RID is `linux-x64`. The existing ancillary AMD64 workflow is blocked unchanged and requires a separate script correction and verification.

`compile-client` appears in wrapper help but is not dispatched by either script. Build the managed Client project using section 7 instead. The batch file's `:Compile-PathFinderBridge` label is also not dispatched, references an absent project and uses `linux-amd64`. PathFinderBridge is obsolete; use the existing PathFinder ARM64 workflow in section 5.

Run image-build actions from `VMTesting/`, since Dockerfile paths are relative to the current directory. Publish mounts each project's `VM_PUBLISHED_ARTIFACTS/` directory at `/VM_PUBLISHED_ARTIFACTS`; Engine runtime commands mount its output at `/roof`. All Engine configurations share that output directory.

VMTesting is licensed under Apache-2.0.

The licensing of these utilities does not remove the obligation to supply applicable compilation scripts and build inputs when required as part of the corresponding source for distributed GPL/AGPL/LGPL applications.

## 12. Release Source and Build Traceability

Each official binary release must identify the source and build inputs used to produce its artifacts.

The release record should include:

- Application version.
- Source commit or release tag.
- Exact corresponding library source revisions.
- .NET SDK version.
- Docker build image identification.
- Required native compilation dependencies.
- Build configuration.
- Runtime identifier.
- Relevant scripts and Dockerfiles.
- Released binary hashes.
- Source availability location.
- Verification results for the applicable modified-library rebuild procedures.

The official release source should enable a recipient to reconstruct the documented build environment and produce a functioning application using modified LGPL-covered code.

Bit-for-bit identical binaries are not required solely to demonstrate LGPLv3 recombination compliance.

## 13. Known Limitations and Future Development

### 13.1 Current Hardware Target

Roof.Engine currently targets the Raspberry Pi 5 deployment environment.

Although some components may compile in other environments, successful compilation does not establish full functionality on unsupported hardware.

### 13.2 Hardware Independence

Following the initial public source release, development will progressively work towards reducing hardware-specific dependencies and improving support for other deployment environments.

This work is not a prerequisite for publishing the current Raspberry Pi 5-targeted implementation.

### 13.3 Build Verification

Build instructions and Docker workflows must be verified against the actual published source revision.

Known build limitations, missing dependencies and platform requirements should be documented rather than represented as supported functionality.

## 14. Related Documentation

- `README.md` — Project overview.
- `LICENSING.md` — Component licensing and LGPLv3 compliance approach.
- `DISTRIBUTION.md` — Binary distribution, source availability and installation information.
- `LICENSES/` — Applicable full license texts.

## 15. References

- [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- [.NET NativeAOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [GNU LGPLv3](https://www.gnu.org/licenses/lgpl-3.0.html)
- [GNU GPLv3](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU AGPLv3](https://www.gnu.org/licenses/agpl-3.0.html)

---

Maintained by Data Foundation Lab.

The build instructions describe Roof's current development and compilation model. Platform support and procedures should be updated as the implementation evolves.
