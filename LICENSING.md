# Licensing

## Overview

Roof is an open-source project developed by Data Foundation Lab (DFLab) as part of its work towards a Personal Internet.

Roof uses a multi-license structure reflecting the different responsibilities and distribution characteristics of its components.

The project includes executable applications, reusable libraries and development utilities. Each component is licensed independently under the terms specified below.

This document describes the applicable licenses, relationships between components, and the intended approach to complying with obligations arising from the incorporation of LGPL-covered libraries into NativeAOT applications.

## 1. License Allocation

The following licenses apply to the respective components:

| Component              | SPDX Identifier | License                                |
| ---------------------- | --------------- | -------------------------------------- |
| Roof.Engine            | GPL-3.0-only    | GNU General Public License v3.0        |
| Roof.PathFinder        | AGPL-3.0-only   | GNU Affero General Public License v3.0 |
| Roof.Client            | LGPL-3.0-only   | GNU Lesser General Public License v3.0 |
| Roof.Common            | LGPL-3.0-only   | GNU Lesser General Public License v3.0 |
| Roof.NativeAppSDK      | LGPL-3.0-only   | GNU Lesser General Public License v3.0 |
| Roof.ClientTestConsole | N/A             | N/A                                    |
| VMTesting              | Apache-2.0      | Apache License 2.0                     |

The publication scope and licensing of any additional utilities, must be established before their public distribution.

Licenses apply according to the component containing the relevant source files. No single repository-wide license supersedes these allocations.

The applicable full license texts are maintained in the repository's `LICENSES/` directory.

All identifiers ending in `-only` refer to the specified version of the license and do not grant an automatic option to use later versions.

## 2. Component Relationships

Roof comprises several independently licensed components that share code or communicate through defined interfaces.

The principal compilation relationships are:

- Roof.Engine references Roof.Common.
- Roof.PathFinder references Roof.Common.
- Roof.Client references Roof.Common.
- Roof.NativeAppSDK references Roof.Common, where included.

Roof.Engine and Roof.PathFinder also communicate through network interfaces.

Network communication is distinct from direct compilation or linking. Communication between these applications does not, by itself, impose the AGPL license of Roof.PathFinder on Roof.Engine or other independently developed network clients.

These relationships describe the assessed architecture and must be reviewed if component dependencies, source sharing or linking arrangements change.

## 3. LGPL-Covered Libraries

Roof.Client and Roof.Common are licensed under LGPL-3.0-only.

These libraries may be used, modified and redistributed subject to the terms of LGPLv3.

Applications incorporating these libraries must comply with the applicable combined-work provisions of LGPLv3, including requirements concerning modification, recombination, notices and reverse engineering for debugging modifications to the covered libraries.

The incorporation of LGPL-covered libraries into GPLv3- or AGPLv3-covered applications does not change the license governing the standalone LGPL-covered library source.

## 4. NativeAOT and Library Recombination

Roof.Engine and Roof.PathFinder use .NET Native Ahead-of-Time (NativeAOT) compilation.

Under this model, retained code from referenced managed libraries may be incorporated into the resulting native executable.

Accordingly, the distributable applications cannot be assumed to support replacing Roof.Common simply by substituting a DLL at runtime.

DFLab's intended LGPLv3 compliance approach is the source-based recombination mechanism described in LGPLv3 section 4(d)(0).

Under this approach, the relevant distribution must provide the applicable Minimal Corresponding Source and Corresponding Application Code in a suitable form, together with the information necessary to enable recipients to recombine or rebuild the application using a modified version of the LGPL-covered library.

For Roof, the intended implementation is to provide complete, version-matched application and library source, together with the relevant build inputs and instructions.

The recipient must be able to modify the LGPL-covered library code and produce a functioning combined application incorporating those modifications.

The availability of public source code alone does not establish that these requirements have been satisfied.

The rebuild procedure must be validated against the applicable release artifacts before binary distribution.

## 5. Corresponding Source and Distribution

For distributions of Roof applications in object-code or native executable form, DFLab intends to make the applicable Corresponding Source available through an appropriate license-compliant distribution mechanism.

The release materials should identify:

- The exact application source revision.
- The corresponding Roof.Common and, where applicable, Roof.Client source revisions.
- Required SDKs, compilers and build tools.
- Required dependency versions and acquisition procedures.
- Build scripts, configuration and target runtime settings.
- Relevant installation and execution procedures.
- The location of applicable license texts and notices.

Each binary release must be traceable to its corresponding source.

A repository's changing default branch is not a substitute for identifying the source corresponding to a particular distributed binary.

Detailed build and release procedures are maintained separately in `BUILDING.md` and `DISTRIBUTION.md`, once those procedures have been implemented and verified.

## 6. Installation Information

LGPLv3 section 4(e) incorporates relevant installation-information requirements from GPLv3 section 6.

These requirements apply in the circumstances specified by those licenses, including certain distributions involving User Products.

Roof may be distributed as software or supplied as part of a preconfigured physical device.

Where the applicable conditions require installation information, DFLab must provide the information necessary to install and execute a modified combined application to the extent required by the relevant license provisions.

This may include instructions concerning software installation, replacement, provisioning, signing or other technical restrictions.

Installation-information obligations are assessed according to the actual distribution arrangement. They are not presumed to apply identically to every distribution channel.

The relevant distribution-specific procedures must be verified before release.

## 7. Roof.PathFinder and Network Use

Roof.PathFinder is licensed under AGPL-3.0-only.

In addition to applicable source distribution obligations, modified versions covered by AGPLv3 section 13 must prominently offer users interacting with the program remotely through a computer network an opportunity to receive the Corresponding Source of the running version, without charge, through the required network-accessible mechanism.

DFLab will maintain a source-availability procedure for applicable deployed PathFinder versions.

The implementation must identify the corresponding source for the version actually deployed.

## 8. Third-Party Components

Roof may incorporate third-party software, libraries, tools or other materials governed by separate licenses.

Those components remain subject to their respective license terms.

Where required, third-party copyright notices, license texts and attribution will be provided through a consolidated `THIRD-PARTY-NOTICES` file or equivalent notice directory.

The licenses listed in this document apply to DFLab's designated project components and do not supersede third-party license obligations.

## 9. Contributions

Contributions to Roof must comply with the license applicable to the component receiving the contribution.

Contributors must have the necessary rights to submit their work under the applicable project license.

Third-party code, dependencies and assets must not be introduced without appropriate license compatibility and attribution review.

Additional requirements will be documented in `CONTRIBUTING.md`.

## 10. License References

The applicable licenses are available in the repository and from their respective publishers:

- [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html)
- [GNU Affero General Public License v3.0](https://www.gnu.org/licenses/agpl-3.0.html)
- [GNU Lesser General Public License v3.0](https://www.gnu.org/licenses/lgpl-3.0.html)
- [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)

## 11. Compliance and Interpretation

This document describes the intended licensing structure and distribution approach for Roof.

Compliance with the applicable licenses depends on the actual source, binaries, build procedures, notices and distribution conditions.

Where this document differs from an applicable license, the terms of that license govern.

Questions concerning licensing or redistribution should be directed to Data Foundation Lab through the project's published contact or issue-reporting channels.
