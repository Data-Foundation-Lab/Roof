# Distribution and Source Availability

## Overview

Roof is an open-source project developed by Data Foundation Lab (DFLab) towards a Personal Internet.

This document describes how Roof is distributed, how corresponding source code is made available, and how recipients may build, modify and install the software under its applicable open-source licenses.

Roof's initial open-source release represents a designated snapshot of its existing internal development implementation. The software is published with its established architecture, functionality, build configurations and deployment procedures.

The initial release is intended to establish a public development baseline from which further engineering and community contributions can proceed.

In particular, improving hardware independence and supporting additional deployment environments are objectives for subsequent public development, rather than prerequisites for the initial source release.

This document describes the distribution model for the initial release and the procedures and obligations that apply when distributing source code, compiled binaries or preconfigured hardware.

## 1. Distribution Scope

Roof uses a multi-license structure reflecting the responsibilities of its individual components.

| Component       | License       | Distribution                                |
| --------------- | ------------- | ------------------------------------------- |
| Roof.Engine     | GPL-3.0-only  | NativeAOT application targeting Linux ARM64 |
| Roof.PathFinder | AGPL-3.0-only | NativeAOT relay application                 |
| Roof.Client     | LGPL-3.0-only | Reusable .NET 10 library                    |
| Roof.Common     | LGPL-3.0-only | Shared .NET 10 library                      |
| VMTesting       | Apache-2.0    | Build and development utilities             |

The licenses apply to their respective components rather than uniformly to the entire repository.

Additional components are governed by the publication scope and licensing arrangements described in `LICENSING.md`.

The incorporation of LGPL-covered libraries into GPL- or AGPL-covered applications does not change the licenses governing the standalone library source.

## 2. Initial Open-Source Release

### 2.1 Release Baseline

The initial public release is based on the existing internal development implementation at a designated frozen source revision.

The release preserves the established project architecture and development workflows without requiring architectural changes solely for public publication.

At this baseline:

- Roof.Engine targets .NET 10 and is designed to operate on Raspberry Pi 5 hardware running Linux ARM64.
- Roof.PathFinder targets .NET 10 and uses the existing Docker-based NativeAOT build workflow.
- Roof.Client and Roof.Common target .NET 10 and are built using the .NET SDK.
- Existing DEBUG, IA, BETA and RELEASE build configurations are retained where defined.
- VMTesting provides the established container-based compilation and testing tools.
- Roof.Engine is deployed to Raspberry Pi 5 using SFTP and executed directly on the device.

The initial release does not guarantee operation on arbitrary hardware platforms or operating systems.

### 2.2 Development After Publication

Following the initial source release, development will work towards reducing hardware-specific dependencies and improving Roof's portability.

This may include changes to hardware integration, operating-system dependencies, deployment procedures and supported execution environments.

Those improvements will be developed through the public project and documented as support is established.

The initial release is the foundation for that work, rather than a hardware-independent implementation.

## 3. Public Source Distribution

### 3.1 Repository Publication

Roof's source code is distributed through its official public repository.

The initial publication includes the approved source components, supporting development and build materials, license texts and relevant documentation.

The published source must correspond to the designated release baseline.

### 3.2 Version Identification

Official source releases should be identified using versioned Git tags and, where appropriate, corresponding release archives.

For example:

```text
v0.1.0
```

The example identifier does not establish that this version has already been published.

Each release must identify its source revision so that recipients can distinguish the released baseline from subsequent development.

The default development branch may change after publication and must not be assumed to correspond to an earlier released binary.

### 3.3 Source Distribution Without Binaries

Where DFLab distributes source code alone, recipients may obtain, modify and build the applicable components under their respective licenses.

The publication of source code alone does not require DFLab to provide precompiled applications for every possible platform.

The absence of a compiled binary for a particular hardware platform does not prevent recipients from adapting the source to that platform, subject to the applicable licenses.

Where binary distributions are subsequently provided, the additional obligations described in this document apply.

## 4. Binary Distribution and Corresponding Source

### 4.1 General Requirements

When DFLab distributes compiled Roof applications, the relevant GPLv3, LGPLv3 and AGPLv3 requirements must be satisfied.

Binary distributions must provide or make available the applicable Corresponding Source through a mechanism permitted by the governing license.

Corresponding Source includes the source and relevant materials required by the applicable license, including required build and installation scripts and appropriate build inputs.

Providing access to the latest repository revision does not, by itself, establish that the source corresponds to a previously distributed binary.

### 4.2 Source-to-Binary Identification

Each official binary release should identify:

- The application version and source revision.
- The corresponding Roof.Common and Roof.Client source revisions, where applicable.
- The .NET SDK and relevant build-environment versions.
- The build configuration and target runtime identifier.
- The required build scripts and Dockerfiles.
- Required dependency versions and acquisition instructions.
- The published binary artifacts and their cryptographic hashes.
- The location of the corresponding source and applicable license materials.

Where all relevant components are built from a single Roof source revision, that relationship may be recorded through the repository's release tag.

Where independently built library binaries are incorporated, their corresponding source revisions must be identified separately.

### 4.3 Source Availability Mechanism

For downloadable binary releases, DFLab intends to provide corresponding source through the public repository's versioned releases or another appropriate source-delivery location.

When relying on GPLv3 section 6(d), the binary download location must provide clear directions to equivalent access to the Corresponding Source at no further charge, in accordance with that provision.

The chosen source-delivery mechanism must satisfy the applicable license, including relevant availability periods.

Any future distribution channels beyond public source repositories and downloadable software binaries will be assessed separately for applicable licensing and source-availability obligations.

The actual distribution mechanism must be assessed before publication or delivery of the affected binaries.

### 4.4 License Notices

Binary distributions must include the applicable license texts, required notices and information identifying relevant LGPL-covered libraries.

Where required by the applicable licenses, recipients must also receive appropriate copyright notices, modification notices and information concerning their rights.

Third-party materials remain subject to their own license and attribution requirements.

The applicable notices will be supplied in the release package or through an otherwise compliant notice-delivery mechanism.

## 5. Roof.Engine Distribution and Installation

### 5.1 Supported Deployment

Roof.Engine's initial supported deployment target is Raspberry Pi 5 running a suitable Linux ARM64 operating environment.

The application is compiled using the Docker-based NativeAOT workflow provided under `VMTesting/`.

The existing workflow produces a Linux ARM64 executable for deployment to the target device.

The build procedures are described in `BUILDING.md`.

### 5.2 Current Installation Procedure

Roof.Engine is currently deployed manually to Raspberry Pi 5 using SFTP.

The established procedure is:

1. Compile Roof.Engine using the existing ARM64 build environment.
2. Transfer the published executable and any required accompanying files to the Raspberry Pi 5 using SFTP.
3. Access the device through a terminal or SSH session.
4. Navigate to the directory containing the executable.
5. Grant executable permission.
6. Execute Roof.Engine.

From the directory containing the executable:

```bash
sudo chmod +x Roof.Engine
sudo ./Roof.Engine
```

These commands reflect the existing execution procedure.

The target device must have the required operating-system configuration, runtime environment, permissions and supporting files for Roof.Engine to function.

The command sequence does not independently configure the Raspberry Pi, install supporting operating-system services or establish persistent startup behaviour.

Those requirements are determined by the Roof.Engine implementation and deployment configuration.

### 5.3 Installing Modified Builds

Recipients may modify Roof.Engine and applicable LGPL-covered libraries under their respective license terms.

The existing compilation and installation approach supports rebuilding the application from source and deploying the resulting executable to Raspberry Pi 5 hardware.

The intended procedure is:

1. Obtain the corresponding Roof source revision.
2. Make the desired source modifications.
3. Build Roof.Engine using the documented Docker-based ARM64 compilation workflow.
4. Transfer the resulting executable and any required accompanying files to an appropriately configured Raspberry Pi 5.
5. Grant executable permission.
6. Execute the rebuilt application.

The manual execution commands remain:

```bash
sudo chmod +x Roof.Engine
sudo ./Roof.Engine
```

A recipient who rebuilds Roof.Engine with modified Roof.Common code must be able to use the supplied source and build materials to produce the relevant combined application.

The release-specific recombination procedure must be verified against the published source and binary distribution.

### 5.4 Deployment Limitations

The initial release retains its existing Raspberry Pi 5-oriented implementation.

The ability to build a Linux ARM64 executable does not establish functional compatibility with every Linux ARM64 device.

Support for additional hardware and deployment environments will be developed separately.

## 6. Roof.PathFinder Distribution

### 6.1 Build and Deployment

Roof.PathFinder is the relay application supporting communication between Roof participants.

Its existing build workflow uses the Docker-based NativeAOT environment.

The supplied VMTesting compilation procedure targets Linux ARM64.

The existing PathFinder deployment and configuration model is retained for the initial source release.

Public source distribution does not provide access to DFLab's production infrastructure, private credentials or operational secrets.

Recipients may develop and operate their own instances using the applicable source code and independently supplied configuration.

### 6.2 Binary Source Obligations

Where PathFinder binaries are conveyed, DFLab must satisfy the applicable AGPLv3 source-distribution and notice requirements.

The corresponding source must be identifiable and accessible through a permitted distribution mechanism.

### 6.3 Network Source Availability

AGPLv3 section 13 includes additional requirements for certain modified versions of covered programs that support interaction with users remotely through a computer network.

Where these conditions apply, the modified program must prominently offer interacting users an opportunity to receive the Corresponding Source of the running version without charge through an appropriate network-accessible mechanism.

DFLab must ensure that applicable PathFinder deployments satisfy this requirement.

The source offer must correspond to the actual deployed version rather than an unrelated or changing development revision.

The network source-offer obligation is separate from the requirements applicable when PathFinder binaries are conveyed.

## 7. Roof.Client and Roof.Common

### 7.1 Independent Libraries

Roof.Client and Roof.Common are licensed under LGPL-3.0-only.

Both projects target .NET 10 and are built using the project's existing .NET SDK workflows.

Their source may be used, modified and redistributed under the applicable LGPLv3 terms.

### 7.2 Use in Combined Applications

Roof.Engine and Roof.PathFinder reference Roof.Common.

Other applications, including RoofDeck, incorporate Roof.Client and Roof.Common.

Where the libraries are incorporated into combined applications, the relevant LGPLv3 conditions must be satisfied.

The licenses of the standalone libraries remain LGPL-3.0-only.

### 7.3 NativeAOT Recombination

Roof.Engine and Roof.PathFinder use NativeAOT compilation.

Retained Roof.Common code may therefore be incorporated directly into their native executables rather than being loaded as a separately replaceable library at runtime.

DFLab's selected compliance approach is LGPLv3 section 4(d)(0), using source-based recombination.

Under this approach, the distribution must supply the applicable Minimal Corresponding Source and Corresponding Application Code in a suitable form, and on suitable terms, for recombining the application with a modified version of the LGPL-covered library.

The intended implementation is to provide complete corresponding application and library source, together with the necessary build inputs and instructions.

Recipients must also be permitted to reverse engineer the combined work for debugging modifications to the LGPL-covered libraries, as required by LGPLv3.

### 7.4 Recombination Procedure

The existing Roof build system uses project references between the affected components.

A recipient can modify Roof.Common and rebuild an application that incorporates it using the existing build workflow.

For Roof.Engine, this uses the VMTesting ARM64 compilation environment.

For Roof.PathFinder, the existing Docker-based compilation workflow is used.

The application must then be installed or executed in an appropriate environment to establish that the modified library code has been incorporated successfully.

The applicable release procedure must be verified using the actual corresponding source and build configuration.

The existence of a functioning internal build system does not replace the need to provide the relevant source and build inputs to recipients of a distributed binary.

## 8. Installation Information

### 8.1 Applicable Requirements

LGPLv3 section 4(e) incorporates relevant Installation Information requirements from GPLv3 section 6.

These requirements apply only in the circumstances specified by the applicable licenses.

The initial Roof open-source transition involves the publication of software source code and, where provided, compiled software binaries. It does not include the distribution of preconfigured hardware devices.

### 8.2 Software Distribution

Roof.Engine currently targets Raspberry Pi 5 hardware running Linux ARM64.

Recipients may obtain the source code, build the application using the documented VMTesting workflow, and manually deploy the resulting executable to their own compatible hardware.

The existing deployment procedure uses SFTP to transfer the executable, followed by:

```bash
sudo chmod +x Roof.Engine
sudo ./Roof.Engine
```

This procedure also provides a means of installing applications rebuilt with modified LGPL-covered library code.

Where applicable, DFLab will provide the corresponding source, build instructions and installation information required under the relevant licenses.

The applicability of additional Installation Information obligations will be assessed if the project's distribution model changes.

## 9. Distribution of VMTesting

VMTesting contains development and compilation utilities used in Roof's build process.

These utilities are licensed under Apache-2.0.

The initial source release includes the relevant build scripts and Dockerfiles used for the published components.

Where these scripts form part of the Corresponding Source required for a GPL-, AGPL- or LGPL-covered binary distribution, their availability must be maintained in accordance with the applicable source obligations.

Their separate Apache-2.0 license does not eliminate those obligations.

## 10. Third-Party Software and Attribution

Roof may include third-party packages, libraries or other materials subject to independent licensing requirements.

Applicable notices and license texts must be determined from the actual materials contained in each source or binary release.

Where required, distributions must preserve relevant:

- Copyright notices.
- License texts.
- Attribution.
- Notices of modifications.
- Other license-specific conditions.

A consolidated `THIRD-PARTY-NOTICES` file or equivalent notice directory will identify relevant third-party materials.

The first-party license allocations described in `LICENSING.md` do not supersede third-party licensing obligations.

## 11. Release Preparation

### 11.1 Source Release

Before publishing an official source release, maintainers should:

1. Identify the designated source revision.
2. Confirm the components included in the publication.
3. Apply the appropriate first-party licenses and notices.
4. Review third-party dependencies and attribution.
5. Remove secrets and private materials not intended for publication.
6. Include the relevant build scripts and documentation.
7. Confirm that the published source reflects the approved release baseline.
8. Create the applicable release identifier or source tag.

### 11.2 Binary Release

Where compiled binaries are distributed, maintainers must additionally:

1. Identify the exact corresponding source.
2. Record the build configuration, runtime identifier and relevant dependencies.
3. Confirm the availability of required build materials.
4. Verify the relevant LGPL library-modification and recombination procedure.
5. Include applicable license and attribution materials.
6. Provide corresponding-source access through a permitted mechanism.
7. Assess Installation Information requirements for the distribution channel.
8. Satisfy applicable AGPLv3 obligations.
9. Record the release artifacts and their hashes.

These are release compliance requirements rather than requirements to redesign the existing implementation.

### 11.3 Release Records

Official binary releases should include sufficient information to identify the source and build environment from which they were produced.

A release record may include:
| Field | Description |
|---|---|
| Version | Published release identifier |
| Source | Repository commit or release tag |
| Build configuration | Applicable project configuration |
| Runtime | Target runtime identifier |
| Toolchain | .NET SDK and native compilation environment |
| Dependencies | Required versions and acquisition information |
| Build scripts | Relevant VMTesting materials |
| Artifacts | Published binaries and hashes |
| Source availability | Location of corresponding source |
| Notices | Applicable first-party and third-party materials |

Release records should be preserved so that previous binary releases remain traceable after development continues.

## 12. Future Distribution Development

The initial public release establishes Roof's existing operational baseline.

Future development may introduce:

- Reduced hardware-specific dependencies.
- Support for additional hardware platforms.
- Broader operating-system compatibility.
- Improved installation and deployment procedures.
- Additional build and packaging workflows.

New distribution methods may introduce different technical or licensing considerations.

The relevant documentation and compliance procedures must be updated as those methods are introduced.

Hardware independence is a development objective, not a condition for distributing the existing Raspberry Pi 5-targeted source.

## 13. Related Documentation

- `README.md` — Project overview and initial release information.
- `LICENSING.md` — Component licenses and licensing relationships.
- `BUILDING.md` — Build procedures and development prerequisites.
- `LICENSES/` — Applicable full license texts.
- `THIRD-PARTY-NOTICES` — Third-party licensing and attribution, where applicable.

## 14. References

- [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU Lesser General Public License v3.0](https://www.gnu.org/licenses/lgpl-3.0.html)
- [GNU Affero General Public License v3.0](https://www.gnu.org/licenses/agpl-3.0.html)
- [.NET NativeAOT Deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)

---

Maintained by Data Foundation Lab.

This document describes Roof's initial open-source distribution model, current deployment methods and applicable source-availability arrangements.

The governing license terms take precedence over any inconsistent description in this document.
