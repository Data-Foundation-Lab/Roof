# Roof - Development Procedures

This document defines the development workflow, review procedures, testing expectations, and maintenance practices for the Roof open-source repository.

These procedures apply to Data Foundation Lab (DFLab) maintainers and external contributors.

For general contribution guidance, see [CONTRIBUTING.md](CONTRIBUTING.md). For security vulnerability reporting, see [SECURITY.md](SECURITY.md).

## 1. Repository Overview

Roof is an open-source project developed by Data Foundation Lab (DFLab) to support user-controlled, local-first personal infrastructure.

The repository contains the following components:

| Component       | Description                                               | License       |
| --------------- | --------------------------------------------------------- | ------------- |
| Roof.Engine     | Device-side software responsible for Roof node operations | GPL-3.0-only  |
| Roof.PathFinder | Connectivity and relay service                            | AGPL-3.0-only |
| Roof.Client     | Client-side integration library                           | LGPL-3.0-only |
| Roof.Common     | Shared types and functionality                            | LGPL-3.0-only |
| VMTesting       | Virtualised testing and development environment           | Apache-2.0    |

Development should preserve compatibility between components, respect their licensing requirements, and avoid unnecessary architectural coupling.

## 2. Branching Strategy

Roof follows a feature-branch development model.

### 2.1 Default Branch

`main` is the primary integration branch.

The default branch is protected through GitHub repository rules and is intended to contain reviewed, integrated changes that satisfy applicable quality requirements.

All routine changes must be proposed through pull requests.

Direct development against the default branch is not permitted under the normal workflow.

### 2.2 Working Branches

Changes should be developed in separate branches created from the latest default branch.

Recommended naming conventions:

| Branch prefix  | Purpose                         |
| -------------- | ------------------------------- |
| `feature/`     | New functionality               |
| `fix/`         | Bug fixes                       |
| `refactor/`    | Internal restructuring          |
| `docs/`        | Documentation changes           |
| `test/`        | Test additions or modifications |
| `build/`       | Build and dependency changes    |
| `maintenance/` | Repository maintenance          |

Examples:

- `feature/engine-network-recovery`
- `fix/pathfinder-reconnection`
- `refactor/client-connection-management`
- `docs/build-instructions`

Branches should be concise, descriptive, and focused on a specific change.

Long-running branches should be avoided where practical.

## 3. Development Workflow

Routine development follows the process described below.

### 3.1 Identify the Work

Development should be associated with an issue, maintenance requirement, or planned engineering task.

Before beginning work, contributors should:

- Review existing issues and related pull requests.
- Identify the affected components.
- Consider compatibility, security, and licensing implications.
- Discuss significant architectural changes with maintainers before implementation.

Minor corrections and documentation changes may proceed without a prior issue.

### 3.2 Create a Working Branch

Create a working branch from the current default branch.

```bash
git switch main
git pull --ff-only origin main
git switch -c feature/example-change
```

External contributors may develop changes through their own repository forks.

### 3.3 Implement Changes

Changes should remain focused on the identified task.

Contributors are expected to:

- Follow existing project conventions and architectural patterns.
- Avoid unrelated modifications.
- Preserve existing functionality unless behavioural changes are intended.
- Update relevant tests and documentation.
- Avoid committing secrets, credentials, or environment-specific configurations.
- Consider compatibility with other Roof components and supported deployment environments.

Changes affecting shared interfaces, network protocols, persistent data formats, or security boundaries require particular care.

### 3.4 Validate Changes

Before opening a pull request, contributors should perform applicable validation.

At minimum:

- Confirm that affected projects compile.
- Run relevant automated tests where available.
- Check for unintended changes in behaviour.
- Verify that no sensitive information has been introduced.
- Confirm compliance with applicable project licenses.

Platform-specific changes should be tested against their intended environments where practical.

Any testing limitations should be disclosed in the pull request.

### 3.5 Submit a Pull Request

Changes must be proposed through a pull request targeting the appropriate protected branch.

Pull requests should explain:

- The purpose of the change.
- The components affected.
- Important implementation decisions.
- Testing and validation performed.
- Known limitations or outstanding work.
- Relevant compatibility, security, or licensing implications.

Related issues should be referenced where applicable.

### 3.6 Review and Merge

Maintainers review proposed changes according to Section 4.

Once applicable approvals and repository protection requirements have been satisfied, an authorised maintainer may merge the pull request.

## 4. Code Review Procedures

Code review is required for routine changes entering the protected default branch.

### 4.1 Review Requirements

Pull requests must comply with the active GitHub branch protection rules and repository rulesets.

The current project policy requires one approving review. [Steve Yoo (@dflbsteve)](https://github.com/dflbsteve) (DFLab Maintainer) owns review, maintenance and final integration decisions as described in [CONTRIBUTING.md](CONTRIBUTING.md) and [MAINTAINERS.md](MAINTAINERS.md). Authors cannot count self-review as approval. Sensitive changes require Steve's explicit assessment, with private handling under [SECURITY.md](SECURITY.md) where necessary.

These may include:

- Required approving reviews.
- Successful required status checks.
- Resolution of review conversations.
- Restrictions on direct pushes and history modification.

The repository's active rulesets determine the technically enforced requirements.

### 4.2 Review Criteria

Reviewers should evaluate changes against the following considerations.

**Correctness**

- Does the implementation address the intended problem?
- Are error conditions handled appropriately?
- Are relevant tests present?

**Architecture**

- Does the change follow established design principles?
- Does it introduce unnecessary dependencies?
- Does it preserve compatibility between components?

**Security and privacy**

- Does the change introduce new attack surfaces?
- Are authentication, encryption, and access boundaries respected?
- Could the change expose user data, credentials, or sensitive information?

**Maintainability**

- Is the implementation understandable?
- Are responsibilities appropriately separated?
- Is relevant documentation updated?

**Licensing**

- Are new dependencies compatible with the applicable component licenses?
- Are attribution and distribution obligations identified?
- Does the change affect LGPL-covered components or their distribution requirements?

### 4.3 Review Outcomes

Reviewers may:

- Approve the pull request.
- Request changes.
- Provide non-blocking comments.
- Recommend deferral or closure with an explanation.

Substantive concerns should be resolved before merging.

Contributions may be declined where they conflict with Roof's architectural direction, security requirements, project scope, or maintenance capacity.

### 4.4 Maintainer Responsibilities

Maintainers are responsible for:

- Reviewing proposed changes.
- Maintaining code quality and architectural consistency.
- Ensuring repository protections remain effective.
- Evaluating security-sensitive and compatibility-impacting changes.
- Managing integration and release decisions.
- Communicating material changes affecting contributors.

## 5. Build and Testing Procedures

Roof primarily uses C# and .NET 10.

Development environments should use the SDK version documented by the repository.

### 5.1 Local Builds

From the repository root, contributors may use:

```bash
dotnet restore Src/Roof.sln
dotnet build Src/Roof.sln -c Debug --no-restore
```

The main solution also declares `IA`, `Beta` and `Release`. The nested Engine and Client solutions declare only Debug and Release; use the main solution or project file for IA/Beta.

The current solution has no automated test projects; Roof.ClientTestConsole is interactive. Where automated test projects are added:

```bash
dotnet test
```

These commands are general examples. Component-specific build configurations, runtime requirements, and documented build instructions take precedence.

### 5.2 Platform-Specific Validation

Roof components have different development and deployment requirements.

- **Roof.Engine:** Validate compatibility with the supported Linux ARM64 target.
- **Roof.PathFinder:** Validate relay behaviour, connection handling, and applicable server deployment requirements.
- **Roof.Client:** Validate client integration and compatibility with dependent components.
- **Roof.Common:** Validate shared interfaces and the effects of changes on dependent projects.
- **VMTesting:** Validate the relevant virtualised build or testing environment.

Successful compilation alone does not establish deployment readiness.

### 5.3 Continuous Integration

Repository CI workflows should perform the checks applicable to proposed changes.

Required checks must pass before merging unless an explicitly authorised exception is permitted by repository governance.

Changes to CI configurations, dependencies, or build tooling require review because they may affect development integrity and release reproducibility.

## 6. Compatibility and Architectural Changes

Roof comprises multiple interacting components. Changes to one component may affect others.

The following changes require additional consideration:

- Network protocols and message formats.
- Identity and authentication behaviour.
- Encryption and key management.
- Client and node registration.
- Filesystem and persistent configuration formats.
- Public library interfaces.
- Relay connectivity and reconnection behaviour.
- Platform-specific operating system integrations.

Pull requests introducing significant architectural changes should document their compatibility implications.

Breaking changes should not be introduced without maintainer review and an appropriate migration or release strategy.

Where relevant, changes should preserve interoperability with previously supported component versions or explicitly document incompatibilities.

## 7. Dependency and Licensing Management

Each Roof component is subject to its designated open-source license.

Contributors must ensure new dependencies and incorporated code are compatible with the relevant component and its intended distribution model.

New dependencies should be assessed for:

- License compatibility.
- Maintenance and security status.
- Runtime and platform requirements.
- Transitive dependencies.
- Required copyright notices and attribution.
- Build and distribution implications.

Changes affecting GPL, AGPL, or LGPL licensing obligations require explicit review.

Contributors must not introduce code or assets without appropriate rights to contribute and distribute them.

Applicable third-party notices must be maintained when dependencies are added, removed, or changed.

## 8. Security-Sensitive Development

Roof processes user-controlled data and supports communication between devices and clients.

Security-sensitive changes require appropriate scrutiny.

Areas requiring particular attention include:

- Authentication and authorisation.
- Device and client identification.
- Cryptographic operations.
- Network communications and relay behaviour.
- Configuration storage.
- Filesystem access.
- Privileged operations.
- Input validation and resource limits.
- Diagnostic output and logging.

Sensitive information must not be committed to the repository.

Known or suspected vulnerabilities should be reported using the process described in [SECURITY.md](SECURITY.md), rather than disclosed through public issues before appropriate coordination.

Security fixes may be developed through a restricted process where public disclosure would create unacceptable risk. Their integration into the public repository must follow an authorised and documented release procedure.

### 8.1 Build Configurations and Hosted Services

| Engine configuration | Update channel used by the code | Startup behaviour |
| --- | --- | --- |
| DEBUG | `ia` if the updater is invoked | Automatic update checks and startup-unit installation are skipped. Local services and periodic setup AP monitoring still start. DEBUG can log packet contents. |
| IA | `ia` | Automatic update checks, startup-unit provisioning and offline setup AP startup are enabled. |
| BETA | `beta` | Same startup behaviour as IA. |
| RELEASE | `pa` | Same startup behaviour as IA. |

All four configurations start the PathFinder client once Engine initialisation reaches service startup. With the original WSS address configured, it automatically connects or retries against the shared hosted relay and announces the Roof identifier. The public source uses a non-URL placeholder for both the relay address and the release server base address. Relay connection attempts and release metadata requests therefore fail until valid addresses are supplied in source. Local networking and host provisioning can still run. The Engine also probes internet reachability with ICMP to `8.8.8.8`, independently of the two placeholders.

The updater reads channel metadata and can replace and restart the Engine executable. It checks the downloaded binary against a SHA-256 hash supplied by the same release service. The release server, its metadata and its upload credentials are therefore part of the trust boundary for deployments with updates enabled.

The standalone PathFinder listens for plain WebSocket connections on all IPv4 interfaces on TCP port 8071. It trusts client-supplied routing identifiers, and a new connection claiming an existing identifier can displace that session. Deployments need their own TLS termination and network access controls. The Engine separately checks its registered clients and their network encryption material. Use a private test relay and network when exercising remote connectivity.

### 8.2 State and Authentication Across Builds

- All four builds use `roof_config/config.json` under the working directory and the same absolute `/etc/Roof/keys/` directory. A different working directory separates the JSON file but still shares the configuration key and the per-client key namespace. The Linux account, SFTP group, shared-storage and service namespaces are also host-wide.
- The Engine generates its configuration key and generates per-client AES-256 keys and IVs during registration. Key files contain raw key and IV bytes. The writer sets root ownership, mode `0700` on the key directory and mode `0600` on files. Keep keys, configuration, logs and storage from real installations out of development environments and release materials.
- Compile flags select release channels and runtime branches. Authentication uses the installation identity, registration records and client key material across configurations. A build that opens an existing installation's state can recognise its clients and overwrite shared state. Client applications using Roof.Client supply the key and IV to the library, which keeps them in process memory. The caller is responsible for any persistent client credential storage.
- Local enrollment depends on network isolation. The enrollment exchange gives an unregistered local client its key and IV over TCP, and the exchange can also enroll additional clients after initial setup. The relay path rejects enrollment.

### 8.3 Privileged Development and Test Execution

Run Engine on disposable Linux ARM64 hardware or in a dedicated test VM with fresh state and an isolated network. This applies to DEBUG and to runs with the public endpoint placeholders. The Engine assumes a Raspberry Pi 5-style Linux environment with systemd, sufficient root or sudo privileges, writable configuration and storage locations, and a trusted executable and working directory.

- Startup can install OpenSSH and ACL tooling through APT, edit `/etc/ssh/sshd_config`, restart SSH, create SFTP groups and shared storage under `/srv/roof/playground`, and migrate existing client storage. IA, BETA and RELEASE can install and enable `/etc/systemd/system/roofenginestartup.service` using the current executable and working directory. Keep the executable and its parent directories protected from unprivileged writes. The unit installer assumes a trusted temporary-file location and a binary path safe to embed in a shell command.
- Client operations can create Linux accounts, change passwords, change ownership and permissions, modify Wi-Fi profiles and reboot the machine. Setup AP monitoring can create or remove `ap0`, modify the `RoofAP` NetworkManager profile and restart NetworkManager. It depends on `wlan0`, compatible hardware and the `nmcli`, `ip`, `iw` and `rfkill` tools. The setup AP is open. Keep it and the Engine's local TCP 8671 and UDP 6969 traffic away from untrusted clients and production networks.
- VMTesting scripts use privileged Docker containers and writable mounts of the source or build artifacts. Runtime commands create a fixed `192.168.0.0/24` bridge and publish SSH and Roof ports without a loopback binding. The runtime recipe sets public test passwords for root and `roof`, enables root SSH login, grants `roof` passwordless sudo and configures console autologin. Treat the Docker host, guest and mounted files as a single test trust boundary. Use a disposable Docker VM with no production mounts, credentials or network access.

Endpoint externalisation, a dedicated RT development PathFinder, environment-specific credential storage and authentication, and safer DEBUG defaults are follow-up development work.

## 9. Release Management

DFLab maintainers coordinate official Roof releases.

Release activities should include:

1. Reviewing the intended release scope.
2. Confirming applicable build and test results.
3. Reviewing compatibility and migration requirements.
4. Verifying licenses and third-party attribution.
5. Preparing release notes.
6. Assigning the appropriate version and Git tag.
7. Publishing approved release artifacts where applicable.

Release versions should follow the project's documented versioning policy.

Developer previews, beta releases, and stable releases must be clearly distinguished.

An official release must be traceable to a specific commit in the public repository.

The publication of source code or acceptance of a pull request does not itself constitute an official release.

## 10. Exceptions and Emergency Changes

Urgent security, reliability, or operational issues may require an expedited review process.

Where practical, emergency changes should continue to use pull requests and automated validation.

Exceptions to ordinary procedures must:

- Be authorised by an appropriate DFLab repository administrator or maintainer with delegated authority.
- Be limited to the scope necessary to resolve the issue.
- Be documented with the reason for the exception.
- Receive retrospective review where normal review could not be completed beforehand.

Emergency procedures must not be used to bypass routine quality controls for convenience.

Any repository protection bypass must comply with the permissions and rules configured in GitHub.

## 11. Documentation and Procedure Maintenance

Development documentation should be updated when changes affect:

- Build requirements.
- Supported platforms.
- Development workflows.
- Repository structure.
- Architectural interfaces.
- Security procedures.
- Dependency or licensing obligations.
- Release processes.

Changes to development procedures should be proposed and reviewed through the repository's normal contribution workflow.

DFLab maintainers are responsible for keeping these procedures consistent with actual repository practices.

---

Maintained by Data Foundation Lab (DFLab)
