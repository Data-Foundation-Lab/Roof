# Third-Party Notices

## Overview

Roof is an open-source project developed by Data Foundation Lab (DFLab) towards a Personal Internet.

Roof uses third-party software, libraries and development tools governed by their respective licenses.

This document identifies third-party materials incorporated into Roof's published source and provides information about their applicable licensing and attribution requirements.

Third-party materials remain subject to their respective license terms. Their inclusion in Roof does not transfer copyright ownership to DFLab or supersede their original licenses.

Roof's first-party licensing arrangements are documented in `LICENSING.md`.

Applicable third-party license texts and notices are maintained in `THIRD-PARTY-LICENSES/`.

## 1. Balena Base Images

**Project:** Balena Base Images  
**Upstream:** https://github.com/balena-io-library/base-images  
**Original source:** https://github.com/balena-io-library/base-images/blob/master/balena-base-images/device-base/raspberrypi5/debian/bookworm/build/Dockerfile  
**License:** Apache License 2.0 (`Apache-2.0`)  
**License file:** `THIRD-PARTY-LICENSES/BalenaBaseImages.LICENSE.txt`

### 1.1 Use in Roof

Roof's `VMTesting/runtime.dockerfile` contains material derived from the Balena Base Images project.

The original Dockerfile was used as a foundation for Roof's Raspberry Pi 5 development and testing environment.

DFLab has adapted the Dockerfile for Roof-specific runtime configuration and development requirements.

The resulting Dockerfile is maintained as part of Roof's VMTesting utilities.

### 1.2 Attribution and Modifications

The derived Dockerfile retains attribution to the original Balena Base Images project and identifies DFLab's modifications.

The source attribution includes:

- Identification of the original project.
- A reference to the original upstream Dockerfile.
- Identification of the applicable Apache-2.0 license.
- Notice that the Dockerfile has been modified by DFLab.

Applicable upstream copyright, attribution and NOTICE information must be preserved as required by the original license.

### 1.3 License

Balena Base Images is distributed under the Apache License, Version 2.0.

A copy of the applicable upstream license is maintained at:
`THIRD-PARTY-LICENSES/BalenaBaseImages.LICENSE.txt`

The license permits use, modification and redistribution subject to its terms, including applicable notice and attribution requirements.

## 2. Build and Development Dependencies

Roof is developed using .NET 10 and uses additional build and development tools.

The established build environment includes, as applicable:

- .NET SDK and associated compilation tools.
- .NET NativeAOT compilation components.
- Docker-based build environments.
- Native compiler and linker toolchains.
- Operating-system development packages.

These tools are used in Roof's compilation and development workflows.

Use of an external development tool does not, by itself, mean that its complete binaries or source code are redistributed as part of Roof's published source.

The licenses governing these tools remain applicable independently of Roof's first-party licenses.

Where Roof redistributes third-party build tools, complete container images or other external software, the applicable licensing and attribution requirements must be assessed separately.

## 3. .NET Libraries and Dependencies

Roof.Engine, Roof.PathFinder, Roof.Client and Roof.Common target .NET 10.

The projects may reference Microsoft .NET framework libraries, NuGet packages and other dependencies governed by separate licenses.

The initial source distribution does not ordinarily include the complete .NET SDK or runtime binaries.

However, when Roof applications are distributed as compiled software, applicable third-party code may be incorporated into the resulting executable, particularly through NativeAOT compilation.

Where DFLab distributes compiled binaries, the relevant third-party components and their licensing obligations must be reviewed against the actual release artifacts.

Required license texts, copyright notices and attribution must be preserved in accordance with their respective terms.

The absence of a separate .NET runtime license file in `THIRD-PARTY-LICENSES/` does not remove any licensing obligations associated with redistributed .NET components.

## 4. Source and Binary Distribution

### 4.1 Source Distribution

Roof's initial public release represents a designated snapshot of its existing internal development implementation.

The source publication includes the approved first-party components and supporting build utilities.

Third-party materials incorporated into the published source must retain their applicable licensing and attribution information.

The Balena-derived Dockerfile and its associated Apache-2.0 license are included in this attribution arrangement.

### 4.2 Binary Distribution

Where DFLab distributes compiled Roof applications, including Roof.Engine or Roof.PathFinder, the applicable third-party licensing requirements must be reviewed against the resulting binaries.

This includes any relevant .NET runtime or other third-party code incorporated through NativeAOT compilation.

Additional license texts or notices will be included with binary distributions as required by the applicable upstream licenses.

Corresponding-source and distribution procedures are described in `DISTRIBUTION.md`.

### 4.3 Container Images

Roof's VMTesting utilities use Dockerfiles and container-based development environments.

Publication of a Dockerfile does not automatically constitute redistribution of the container image produced from it.

If DFLab distributes complete container images, the software included in those images must be assessed for applicable license and attribution requirements.

This includes relevant operating-system packages, libraries and other included third-party components.

## 5. Third-Party License Directory

The initial third-party license directory contains:

```text
THIRD-PARTY-LICENSES/
└── BalenaBaseImages.LICENSE.txt
```

Additional license and notice files may be introduced where required by the actual distributed source or binary artifacts.

The third-party license directory is maintained separately from Roof's first-party `LICENSES/` directory.

The first-party license allocations are documented in `LICENSING.md`.

## 6. Review and Maintenance

Third-party licensing and attribution must be reviewed when:

- Third-party source material is incorporated or modified.
- New libraries or dependencies are introduced.
- Existing dependencies are updated.
- NativeAOT binary distributions are prepared.
- Container images or additional development tools are redistributed.
- New distribution methods are introduced.

The review must reflect the actual materials being distributed rather than assuming that every build-time dependency is included in the final application.

Required upstream notices and license texts must be retained and updated where applicable.

## 7. References

- [Balena Base Images](https://github.com/balena-io-library/base-images)
- [Balena Base Images License](https://github.com/balena-io-library/base-images/blob/master/LICENSE)
- [Original Raspberry Pi 5 Dockerfile](https://github.com/balena-io-library/base-images/blob/master/balena-base-images/device-base/raspberrypi5/debian/bookworm/build/Dockerfile)
- [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- [.NET Runtime](https://github.com/dotnet/runtime)

---

Maintained by Data Foundation Lab.

Third-party materials remain subject to their respective upstream licenses. Where this document differs from an applicable license, the governing license terms take precedence.
