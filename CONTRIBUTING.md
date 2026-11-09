# Contributing to Roof

Roof welcomes focused contributions from DFLab and the wider community. [Steve Yoo (@dflbsteve)](https://github.com/dflbsteve) (DFLab Maintainer) is responsible for contribution review, maintenance and final integration. Participation is subject to [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Choose the Right Channel

- Use [Roof issues](https://github.com/Data-Foundation-Lab/Roof/issues) for public bugs, proposals and development questions concerning Engine, PathFinder, Client, Common, NativeAppSDK or VMTesting.
- Report suspected vulnerabilities privately under [SECURITY.md](SECURITY.md), including issues whose security impact is uncertain. Do not put exploit details, credentials, private logs or user data in public issues or pull requests.

Check existing issues before opening another. For bugs, provide the source revision, component, platform, expected and actual behaviour, and a minimal reproduction using synthetic data. For features, explain the use case and compatibility implications. Discuss substantial architecture, protocol, storage-format, dependency or platform changes with the maintainer before implementation. Small corrections need no prior issue.

## Contribution Categories

Contributions may address bugs, features, documentation, tests, build tooling and dependencies, refactoring, accessibility, or maintenance. Portability and hardware independence are development objectives; proposed support for a new platform needs evidence and maintainer review. Acceptance depends on project scope, correctness, licensing and capacity to maintain the result.

The public label vocabulary and maintainer triage responsibilities are defined in [MAINTAINERS.md](MAINTAINERS.md#public-issue-labels). Contributors do not need label permissions to participate. Suspected vulnerabilities follow the private process regardless of category.

## Licensing and Contributor Certification

Follow [LICENSING.md](LICENSING.md), the applicable files in [LICENSES/](LICENSES/), and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). There is no replacement repository-wide software license.

| Destination | Existing license |
| --- | --- |
| Roof.Engine | GPL-3.0-only |
| Roof.PathFinder | AGPL-3.0-only |
| Roof.Client, Roof.Common, Roof.NativeAppSDK | LGPL-3.0-only |
| VMTesting | Apache-2.0 |
| Roof.ClientTestConsole | N/A in the existing allocation; no license grant established |

Submit original changes under the existing license for their destination. Preserve third-party notices and identify the source, version, license and attribution for new dependencies, copied code and assets. Steve reviews compatibility and distribution implications, including NativeAOT/LGPL recombination, before acceptance. A cross-component change must respect each component's terms.

Every contributed commit must include a sign-off certifying the [Developer Certificate of Origin 1.1](https://developercertificate.org/):

```text
Signed-off-by: Your Name <your-email@example.com>
```

Use `git commit -s` to add it after reading the certificate. A sign-off is a certification of contribution rights, not a cryptographic signature or copyright assignment. No separate contributor license agreement is required. Ensure you also have any necessary employer permission. The name and email in a public commit become part of its public history; choose an appropriate contribution address.

This policy does not assign a new license to existing unallocated material. Contributions to ClientTestConsole or standalone documentation/assets without an established license require Steve to record the applicable terms and necessary rights before acceptance or public distribution. Do not infer permission from their presence in the checkout.

## Prepare and Validate a Change

Follow [DEVELOPMENT.md](DEVELOPMENT.md) and [BUILDING.md](BUILDING.md). Work in a focused branch or fork and submit a pull request against the intended integration branch. Match existing conventions, avoid unrelated changes, and update relevant documentation and tests where available.

For affected managed libraries, build the relevant project without launching Engine; for example:

```bash
dotnet build Src/Roof.Client/Roof.Client.csproj -c Debug
dotnet build Src/Roof.NativeAppSDK/Roof.NativeAppSDK.csproj -c Debug
```

Report what you actually built or exercised, including the platform, configuration and revision. Do not claim runtime, hardware or security validation from compilation alone. There are currently no automated test projects; ClientTestConsole is interactive. Add meaningful tests where appropriate and disclose any validation you could not perform.

## Existing Development Limitations

Developer Preview 0.1.0 is experimental and source-only. It provides no production support guarantee, packaged applications, complete independent deployment or fully reproducible release workflow. Engine targets Raspberry Pi 5/Linux ARM64, with host provisioning and infrastructure assumptions. Hardware independence remains future work.

Engine can change accounts, passwords, SSH, permissions, networking and startup services, replace its executable and reboot even when development builds are involved. Use disposable hardware or a dedicated test VM with isolated networking and no production credentials or mounts. VMTesting uses privileged Docker containers and writable host mounts; run its workflow inside a disposable VM. Build configuration alone does not isolate host state.

Endpoints are source placeholders; independent relay/release infrastructure is not supplied. Security/device checks include stubs, and current protocol, shared-storage and platform limitations remain material. Read the current limitations in [README.md](README.md#known-limitations) and the execution boundaries in [DEVELOPMENT.md](DEVELOPMENT.md#8-security-sensitive-development) before running anything. Never commit real keys, installation configuration, passwords, private logs or user files.

## Review and Approval

A pull request should describe the problem, resulting behaviour, affected components, related public issues, validation, limitations, and compatibility/security/licensing implications. Keep sensitive evidence in the private security channel.

Routine changes require one approving review under the current project policy, resolution of blocking concerns, and any checks required by active GitHub rules. Steve owns final approval and merge decisions; he may delegate review to an appropriate reviewer while retaining accountability. Authors must not count their own approval. If Steve authors a change, another eligible reviewer is needed; a sole maintainer does not satisfy the approval requirement by self-review.

Security-sensitive, licensing, architecture, protocol, persistence and release changes need Steve's explicit assessment recorded in the appropriate public or private review. Specialist advice may be needed; this does not establish a second mandatory approval. Substantive changes after approval need renewed review. Active repository rules determine the technical enforcement and must be satisfied.

Review may result in approval, requested changes, deferral or closure with reasons. Contributions are not guaranteed acceptance or a response deadline. Emergency exceptions follow [DEVELOPMENT.md](DEVELOPMENT.md#10-exceptions-and-emergency-changes), with a recorded justification and retrospective review. Merging is distinct from publishing an official release.
