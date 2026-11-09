# Roof

**Personal World-Wide Internet - Decentralized, Federated, and Eternally Connected Data Storage and Internet.**

Roof is an open-source Personal Internet project developed by Data Foundation Lab (DFLab). Its development brings together user-controlled storage, connectivity and applications around personally operated Roof nodes.

This is Roof's core software repository. It contains Roof.Engine, Roof.PathFinder, Roof.Client, Roof.Common, Roof.NativeAppSDK and the supporting development utilities. RoofDeck, the desktop application for interacting with Roof, is maintained in a separate repository. The component descriptions below document the existing implementation and its relationships within that system.

## Developer Preview 0.1.0

**Developer Preview 0.1.0 is the initial public source release. It is experimental and source-only, with no executable distributions, installers, preconfigured device images or independently supported deployment workflows.** It makes the existing Roof implementation available as the starting point for public development. The release retains the established architecture and workflows; portability, configuration isolation, deployment tooling and comprehensive build reproducibility remain subsequent development work.

This baseline was originally developed to support DFLab's work on Roof Hardware, with the current Engine targeting Raspberry Pi 5 hardware running Linux ARM64. Its operating-system integrations and hardware assumptions reflect that work. The source is now being released to support development toward a hardware-agnostic Roof implementation, reducing dependence on specific hardware and enabling broader deployment environments. Hardware independence is a development objective; it is not a capability already delivered by this baseline.

The published implementation has hardware assumptions, unfinished functionality and dependencies on separately operated infrastructure. Building it does not establish that it can be deployed independently or safely on an arbitrary machine. Full independent reproducibility, self-hosting support and safe general-purpose execution are not prerequisites for this initial source-publication milestone.

The preview identifier describes the public source baseline. Existing assembly versions and IA/BETA channel version strings are internal component versions; they are not evidence of a stable public release. References to binary distribution in [DISTRIBUTION.md](DISTRIBUTION.md) describe procedures and obligations for any future binary conveyance, not downloads supplied with this preview.

> **Host safety warning:** Do not run the existing Roof.Engine on production or general-purpose machines without appropriate isolation and a full understanding of its host-level operations. This includes DEBUG. Engine can install packages, edit SSH configuration, create Linux accounts, change passwords (including root), change filesystem permissions, configure networking, install a startup unit, replace its executable and reboot the machine. Use disposable hardware or a dedicated test VM with fresh state, isolated networking and no production credentials or mounts. Privileged Docker containers alone are not an adequate isolation boundary from their Docker host.

## Architecture

The following views describe the Developer Preview 0.1.0 implementation. They distinguish application processes, network paths, separately operated infrastructure and Engine's privileged Linux host integrations. Hardware-independent deployment and broader federation remain roadmap objectives.

### System and Communication Boundaries

![Roof architecture: system and communication boundaries](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BEXTERNAL-PUBLIC%5D%20Roof%20Architecture%20Diagram%20A.png)

Solid arrows show communication or runtime interactions; arrowheads indicate message or operation direction, not which endpoint opens the socket. RoofDeck is a separate application and is not included here. The SDK uses its local bridge rather than opening an independent Engine session. The host application supplies Roof.Client's remote provider. Local operation can continue while Engine retries a failed PathFinder connection.

Enrollment occurs locally; Engine rejects registration through PathFinder. The relay routes by announced identifiers and does not provide an account authentication authority, persistent storage or an offline queue. Direct SFTP reaches the same shared playground through SSH and is not tunneled by PathFinder. Proxy, TLS and release hosting are deployment dependencies, not infrastructure supplied by this repository.

### Engine Services and Host Integration

![Roof architecture: Engine services and host integration](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BEXTERNAL-PUBLIC%5D%20Roof%20Architecture%20Diagram%20B.png)

This view groups responsibilities and key interactions rather than listing every method call. Dashed arrows indicate library dependencies. Roof.Common is also referenced by Roof.Client, Roof.NativeAppSDK and Roof.PathFinder. Engine's resource service currently maintains logs; CPU, memory and other resource metrics are not implemented. Security and device checks remain stubs. The host operations shown here reflect the current appliance-oriented implementation; see the host safety warning above and the roadmap below for the planned separation of runtime services from privileged provisioning.

| Project | Existing responsibility | Direct project reference |
| --- | --- | --- |
| [Roof.Engine](Src/Roof.Engine/) | Node startup and host provisioning; configuration and client registration; command dispatch; shared storage; setup AP; relay connectivity; updates and log maintenance | Roof.Common |
| [Roof.PathFinder](Src/Roof.PathFinder/) | In-memory WebSocket connection registry, identifier-based packet forwarding, heartbeats and session replacement | Roof.Common |
| [Roof.Client](Src/Roof.Client/) | Reusable managed client library: LAN discovery, enrollment, encryption material in memory, local commands and externally supplied relay connection | Roof.Common |
| [Roof.Common](Src/Roof.Common/) | Request/response and filesystem data types, serialization, TCP/UDP/WebSocket providers, crypto helpers, logging and network discovery | None; NuGet dependency on System.IO.Hashing 9.0.2 |
| [Roof.NativeAppSDK](Src/Roof.NativeAppSDK/) | Native application integration through a local RoofDeck bridge, with filesystem requests and callbacks | Roof.Common |
| [Roof.ClientTestConsole](Src/Roof.ClientTestConsole/) | Interactive development harness used before integration into RoofDeck | Roof.Client and Roof.Common |
| [VMTesting](VMTesting/) | Docker compilation wrappers and a Pi-oriented Linux runtime approximation | Builds source projects and mounts published artifacts |

Engine's `ClientManagerService` accepts local traffic and periodically monitors the setup AP. `PathFinderService` connects outward to the relay, retries connections and handles remote clients. `UpdateService` checks for updates in non-DEBUG builds. `ResourceManagerService` currently maintains logs; the broader resource-monitoring comments are not implemented metrics. Managers handle configuration, client state, filesystem commands and OS operations. See [Engine startup](Src/Roof.Engine/Program.cs) and [service registration](Src/Roof.Engine/Services/ServiceManager.cs).

## Current Features

The source contains implementations for:

- Local presence discovery on UDP 6969, local command connections on TCP 8671, local enrollment and registered-client connections.
- Per-client AES key/IV generation, encrypted command payloads, credential rotation and encrypted installation configuration.
- Linux account creation, client and root password changes, shared SFTP access and a common playground filesystem.
- Directory lookup, creation, update, copy and deletion; file lookup, creation, update, deletion and whole-file data retrieval.
- Wi-Fi scanning, connection, current-network lookup and forgetting, setup AP management and machine restart commands.
- Remote command forwarding through PathFinder, with heartbeat/disconnection handling and reconnect attempts.
- Channel-based Engine update checks, downloaded-executable hash checks, replacement and restart, subject to external infrastructure.

These are implemented experimental capabilities, not a production support or security guarantee. In particular:

| Area | Current platform / limitation |
| --- | --- |
| Engine runtime | Intended for Raspberry Pi 5, Linux ARM64, with a Debian-style userspace and systemd. Native Windows/macOS Engine operation and arbitrary Linux hardware are not supported. |
| PathFinder | Supplied NativeAOT workflow targets Linux ARM64. The source is a .NET console server, but no broader server-platform deployment matrix is established. |
| Client/Common/NativeAppSDK | .NET 10 managed libraries, buildable with an appropriate SDK on Windows, Linux or macOS. OS networking tools, interface selection and permissions affect runtime behavior; this is not a guarantee for every OS/CPU combination. No mobile, browser or native C ABI distribution is supplied. |
| Developer tooling | Unix shell wrapper for Linux/macOS and batch wrapper for Windows; Docker must execute Linux ARM64 containers natively or through configured emulation. A wrapper's host OS is not an Engine runtime target. |

### Known Limitations

- `GET_DIRECTORIES` is explicitly disabled and returns an invalid-operation response. Individual directory lookup is available. Symbolic-link copying is rejected by the filesystem implementation.
- `SecurityManager.PeformSecurityAndIntegrityCheck` and `DeviceManager.PerformDeviceCheck` currently log messages only. They do not validate security, install a firewall or certify attached hardware.
- Command restrictions for fresh/setup-resuming clients remain a TODO in the command dispatch table. There is no documented production role/administrator separation for the host-management commands.
- Filesystem clients share one playground. Linux account creation and an SFTP chroot do not provide private per-client storage within it.
- File data retrieval reads the whole file into memory; it is not a streaming or resumable large-file transfer system. Engine's remote client payload limit is 16 MiB.
- PathFinder trusts announced routing identifiers. A connection claiming an existing identifier can replace that session; a routing ID is not an authenticated account identity. Its registry is process-local, with no distributed routing or failover coordination.
- PathFinder's custom HTTP upgrade parser reads the handshake once into an 8192-byte buffer and performs limited header checks. Fragmented handshakes and alternate header formatting can fail; it is not a general HTTP server.
- Network discovery depends on local interfaces, reachability and OS tools. macOS deliberately avoids the active ICMP subnet sweep used elsewhere. Routed/container networks may prevent LAN discovery even when a port is published.
- SSH provisioning assumes Debian paths and service names (`ssh`/`sshd`) and may conflict with an existing SSH configuration, including other SFTP subsystem declarations. It is not a portable SSH configuration manager.
- The startup unit in non-DEBUG builds detaches Engine using a shell, `setsid` and `nohup`-style redirection. It uses `Type=oneshot`, `RemainAfterExit=yes`, `KillMode=none` and no restart policy. Do not assume systemd tracks or stops the resulting Engine process as a normal supervised service.
- Endpoint configuration, development credentials and host namespaces are not separated by build configuration. The public endpoints are placeholders, and independent infrastructure provisioning is incomplete.
- The ancillary AMD64 Docker recipe and test-console path use the invalid .NET RID `linux-amd64` (the .NET x64 RID is `linux-x64`). `compile-client` appears in shell usage but is not dispatched. The batch file also contains an obsolete, undispatched PathFinderBridge label referencing an absent project.
- There are no automated test projects in the current solution; ClientTestConsole is interactive. VMTesting does not establish hardware correctness or production readiness.

This preview supplies source for the components documented here. It does not supply a supported independent deployment, a production-hardened general-purpose server appliance or packaged end-user applications. The current feature list and limitations describe this release; the roadmap describes subsequent development.

## Developer Prerequisites

Read [BUILDING.md](BUILDING.md) for the full workflow and [DEVELOPMENT.md](DEVELOPMENT.md#8-security-sensitive-development) before executing Engine or VMTesting.

For managed builds, install the **.NET 10 SDK**, with access to restore NuGet packages. A compatible C# IDE is useful for the interactive test console. No exact SDK patch is pinned by a `global.json` in this repository.

From the repository root, build libraries without launching Engine:

```bash
dotnet --info
dotnet build Src/Roof.Client/Roof.Client.csproj -c Debug
dotnet build Src/Roof.NativeAppSDK/Roof.NativeAppSDK.csproj -c Debug
```

These build Roof.Common through project references. Ordinary library builds produce managed DLLs, not `.so`/`.dylib` exports. The full solution can be restored and built with:

```bash
dotnet restore Src/Roof.sln
dotnet build Src/Roof.sln -c Debug --no-restore
```

Building does not run Engine. The main solution declares `Debug`, `IA`, `Beta` and `Release`; nested Engine/Client solutions declare only Debug and Release, so use a project file or the main solution for IA/Beta.

The established Engine and PathFinder publication workflow uses Docker, the `.NET 10 SDK / Ubuntu Noble` image, Clang, LLVM and zlib development files. Docker needs Linux ARM64 support and access to package/image registries. **The supplied compilation commands run privileged containers with writable source and output mounts; use a trusted, disposable Docker VM.**

From `VMTesting/`, on Linux/macOS:

```bash
bash roof-vm.sh build-compile arm64
bash roof-vm.sh compile-engine
bash roof-vm.sh compile-pf
```

On Windows, use `roof-vm.bat` with the same arguments. Engine outputs to `Src/Roof.Engine/VM_PUBLISHED_ARTIFACTS/`; PathFinder outputs to `Src/Roof.PathFinder/VM_PUBLISHED_ARTIFACTS/`. Engine actions `compile-engine-ia`, `compile-engine-beta` and `compile-engine-release` share and overwrite the same output directory. `compile-pf` supplies no `-c`; .NET 10 publishing for this target defaults to Release. Both workflows target `linux-arm64`.

The existing device workflow transfers built files over SFTP and executes Engine with root privileges on an already prepared Pi. This is a development procedure, not a complete installer: it does not provision the necessary hardware, OS, network, infrastructure or access policy. This README intentionally provides no general-purpose host run command.

## DEBUG, IA, BETA and RELEASE

These are compile configurations, not isolated environments or evidence of public release maturity. Project configuration names are `Debug`, `IA`, `Beta`, `Release`; the source uses symbols `DEBUG`, `IA`, `BETA`, `RELEASE` and the scripts use uppercase names.

| Configuration | Meaning | Engine update channel | Existing Engine behavior |
| --- | --- | --- | --- |
| DEBUG | Debugging | `ia` if updater is invoked manually | Skips automatic update checks, startup-unit installation and immediate offline AP startup. Still provisions SSH/storage, starts local and relay services, and periodically monitors the AP. Can log packet contents. |
| IA | Internal Availability | `ia` | Startup update check when internet is detected; background checks after setup; startup-unit provisioning; immediate offline AP setup. |
| BETA | Beta Testing | `beta` | Same startup/update branches as IA, using the beta channel. |
| RELEASE | Release configuration | `pa` | Same startup/update branches as IA, using the public-availability channel. Not a stable-release guarantee. |

All configurations share the same key directory, Linux users/group, SSH configuration, storage and service names. Changing the working directory only separates working-directory state; it does not create a separate installation. Builds can recognize existing clients or modify an existing installation's host state.

Once service startup is reached, every configuration starts the PathFinder connection/retry loop and announces the Roof identifier when connected. Historically configured hosted services are shared dependencies, not a dedicated development sandbox. The published source replaces their addresses with `PATHFINDER_PUBLIC_URL` in [PathFinderService.cs](Src/Roof.Engine/Services/PathFinderService.cs) and `RELEASE_CHANNEL_BASE_URL` in [ReleaseServerIngressManager.cs](Src/Roof.Engine/Managers/ReleaseServerIngressManager.cs). These are non-URL source placeholders, not environment-variable substitutions. Remote connectivity and update requests fail until suitable addresses are supplied and the code rebuilt. Host provisioning can still execute with those placeholders unchanged.

Engine separately pings `8.8.8.8` to infer internet availability. Blocking ICMP can therefore affect update/AP decisions even if HTTP connectivity works. Endpoint externalization, an RT development relay, separate development credential storage/authentication and safer DEBUG defaults remain follow-up work.

## PathFinder and Release Infrastructure

### PathFinder Ingress

[Roof.PathFinder](Src/Roof.PathFinder/WebSocketRelay.cs) listens for **plain WebSocket traffic on all IPv4 interfaces, TCP 8071**. It performs its own HTTP/1.1 upgrade and forwards Roof WebSocket packets by sender/destination identifier. It does not terminate TLS or configure DNS, certificates, Cloudflare or a reverse proxy.

The Cloudflare-facing deployment model has the following boundaries:

```text
Engine / remote client -- WSS --> Cloudflare-facing public hostname
    --> reverse proxy / TLS ingress --> plain WS :8071 --> PathFinder

Engine -- HTTPS --> release-service ingress --> channel metadata and binaries
```

This describes deployment assumptions, not infrastructure shipped or verified here. PathFinder reads `CF-Connecting-IP` and `CF-Ray` from incoming headers for origin/ray diagnostics. They are optional metadata and are not authentication. If used, Cloudflare and the origin reverse proxy must preserve the intended headers, while direct access to the backend must be restricted so arbitrary callers cannot supply trusted-looking metadata.

Operators must separately supply the hostname, DNS/Cloudflare configuration, certificates, TLS policy and reverse-proxy routes. A proxy must forward WebSocket upgrade requests to port 8071, preserve the upgrade headers in the format accepted by the current parser, support long-lived bidirectional traffic and allow heartbeats. The relay sends heartbeats about every five seconds and expires inactive sessions at about fifteen seconds. Short proxy timeouts, restarts or connection limits can interrupt sessions.

The public connection should use WSS with a certificate trusted by .NET clients. Any TLS hop between Cloudflare and the origin is also external to PathFinder; the source does not establish an origin certificate or Cloudflare SSL mode. The final plain-WS hop must remain within a trusted network boundary. Port 8071 should not be treated as a hardened public TLS endpoint. A different private test ingress can be supplied, but no complete supported self-hosting recipe accompanies the preview.

### Release Service

The repository contains the Engine's release-service consumer, **not the release server implementation or its deployment configuration**. Cloudflare/proxy routing, TLS termination, content hosting, upload credentials and service access controls must be provided separately; their live settings cannot be established from this checkout.

The consumer expects:

- `<base>/<channel>/artifact_versions.json`, with an `artifacts` array containing a `RoofEngine` entry and `Version` / `LINUX_ARM64_Hash` fields.
- `<base>/<channel>/roofengine/<version>/LINUX_ARM64/Roof.Engine`, where channel is `ia`, `beta` or `pa`.
- A Linux ARM64 artifact even though the runtime download selection tests Linux rather than validating the CPU architecture.

The updater downloads an executable, checks SHA-256 against metadata from that same service, replaces the installed binary and restarts it. This is not independent signed-update verification. Protect the service, metadata, upload path, DNS/TLS and all credentials with authority to publish updates: they can influence code subsequently run with Engine privileges. No private service credentials, access to DFLab infrastructure, or executable artifacts are supplied by this source preview.

## Engine Host, Filesystem and Access requirements

### Operating System and Hardware

Engine assumes a Pi 5-style Linux ARM64 environment with a writable persistent filesystem, systemd, Bash, root access and working noninteractive `sudo` calls. Some operations use direct privileged file I/O rather than sudo, so running as an ordinary account with a few sudo permissions is not an established supported setup. Debian/APT tooling and account utilities (`getent`, `adduser`, `groupadd`, `usermod`, `chpasswd`) are used directly. Storage must support Unix ownership, permissions, setgid directories and POSIX default ACLs (`setfacl`); a filesystem without those semantics is unsuitable for the existing model.

NetworkManager, D-Bus and `nmcli`, `ip`, `iw`, `rfkill` and Wi-Fi scanning tools are required for the Wi-Fi paths. The code assumes a wireless interface named `wlan0` and a driver capable of creating an AP interface `ap0`. The profile is named `RoofAP`, advertises **Roof Setup**, uses the 2.4 GHz `bg` band and IPv4 shared mode, and has no Wi-Fi password. AP monitoring runs approximately every fifteen seconds. Recovery can unblock Wi-Fi, change radio/interface state, recreate AP configuration and restart NetworkManager. This can disrupt the host's existing connectivity.

A container without physical Wi-Fi/driver access cannot validate these operations. Neither successful ARM64 compilation nor a Pi-labeled container establishes compatibility with another board, network device or Linux distribution. Device checks are stubs; do not rely on Engine to reject incompatible hardware before it changes host state.

### Linux Accounts, Shared Storage and SFTP

Client setup creates Linux accounts and adds them to `roofsftpusers`. Client IDs may be used as Linux usernames. Password-management commands use `chpasswd`, including a command that changes root's password. These are real host accounts and credentials.

Engine ensures `/srv/roof/playground` exists, sets `/srv/roof` to root ownership and mode `0755`, applies group `roofsftpusers` recursively to the playground, grants group read/write/traversal, removes access for others, sets directory setgid bits and default ACLs for group inheritance. Created directories use `2770`; created files can use `0770`. Startup can migrate legacy `/home/<username>/playground` content into the shared location. Back up any test state that matters before exercising migrations.

Engine installs/checks OpenSSH and ACL tooling through APT, edits `/etc/ssh/sshd_config` and restarts SSH before accepting client requests. It expects `/usr/lib/openssh/sftp-server` and configures a `Match Group roofsftpusers` block with:

```text
ChrootDirectory /srv/roof
ForceCommand internal-sftp -d /playground -u 0007
X11Forwarding no
AllowTcpForwarding no
```

The chroot and its parents must satisfy OpenSSH ownership/access requirements, including being protected from group/other writes. All group members enter the same playground and can modify shared content. This is not tenant isolation. SFTP uses the separately exposed SSH service (normally TCP 22), not the PathFinder connection. Review the resulting SSH configuration and network exposure in the isolated test environment.

### Keys, Configuration and Privileged Paths

| Location | Existing contents / access requirement |
| --- | --- |
| `/etc/Roof/keys/roof_config.key` | Configuration AES key and IV. Writer sets root ownership and mode `0600`. |
| `/etc/Roof/keys/<clientID>.key` | Per-client network key and IV, also root-owned `0600`. |
| `/etc/Roof/keys/` | Shared absolute key namespace in every build; writer enforces root ownership and `0700`. Protect `/etc/Roof` and ancestor paths as well. |
| `<working-directory>/roof_config/config.json` | Encrypted configuration bytes despite the `.json` name: installation ID, setup state and registered clients. Protect the directory from replacement or unauthorized writes. |
| `/srv/roof/playground` | Shared client data, group permissions and default ACLs as described above. |
| `/etc/ssh/sshd_config` | Privileged SSH/SFTP configuration modified by Engine. |
| `/etc/systemd/system/roofenginestartup.service` | Non-DEBUG startup unit, installed root-owned `0644`, referencing the current executable and working directory. |
| `/tmp/roofenginestartup.service.tmp` | Fixed temporary unit staging path. The existing installer assumes this location is trusted. |
| Executable, working directory, logs and update artifacts | Must be protected from unprivileged modification; Engine can execute/re-execute privileged code from these locations. |

Each key file stores **48 raw bytes: a 32-byte AES-256 key followed by a 16-byte IV**. These files are not themselves encrypted or held in a hardware vault. Configuration encryption and its stored SHA-256 integrity field do not replace OS access controls. Missing keys, failed decryption or integrity mismatches can block startup and require manual recovery. Preserve configuration and matching keys together when backing up isolated test installations.

Network encryption uses the existing AES helpers and stored IVs; it is not a reviewed authenticated key-exchange system. Local enrollment sends the new client's key/IV over the initial TCP exchange before encryption is established and can enroll additional clients after initial setup. Remote enrollment through the relay is rejected. Only allow enrollment on a trusted isolated network. Roof.Client holds caller-supplied key material in process memory; persistent client credential storage belongs to its consuming application.

Keep real keys, configuration, passwords, user files and logs out of source control and development images. DEBUG can log packet data. Protect the binary and all directories from which privileged startup/update code is loaded; changing only the working directory does not isolate the host-wide keys, accounts or services.

## VMTesting

VMTesting is a development aid for NativeAOT compilation and partial Linux runtime testing. Despite its name, its scripts create Docker containers and a Docker bridge; they do not create a fully isolated virtual machine. See [VMTesting/readme.md](VMTesting/readme.md) for commands and shutdown behavior.

The runtime image uses ARM64 Debian Bookworm with systemd, SSH and development tools. It deliberately sets public test credentials (`root:root` and `roof:password123`), permits root SSH login, gives `roof` passwordless sudo and configures console autologin. **Do not expose this environment to untrusted networks.** These defaults are unsuitable for deployment.

The wrappers use `--privileged` and writable mounts. Runtime actions mount `Src/Roof.Engine/VM_PUBLISHED_ARTIFACTS/` at `/roof`, so files written there persist on the Docker host. They create `roof-vm-net` on `192.168.0.0/24` with gateway `.1`; ordinary runtime uses `.253`, full runtime `.254`. This fixed subnet can conflict with an existing LAN. They publish host TCP 22/8671 and UDP 6969/8671 without a loopback-only binding. Published ports do not guarantee LAN discovery through Docker networking.

`run-engine` starts Engine in a Bash-based container without systemd as PID 1, so service operations may fail. `run-engine-full` starts a detached container named `roof-runtimeVM` with the image's systemd command and then executes Engine; it requires suitable Docker/systemd/cgroup support. Leaving its interactive shell does not stop the detached container; stop it with `docker stop roof-runtimeVM`. Removed containers leave mounted host artifacts intact.

The runtime does not supply or validate physical Pi peripherals, `wlan0`/AP driver behavior, complete NetworkManager integration, production routing/TLS, external relays or release hosting. A privileged runtime can affect its Docker host and mounted files. Run the entire workflow inside a disposable VM with isolated networking and no valuable host mounts; do not treat it as a production deployment method.

## Roadmap

![Roof open-source development public roadmap timeline for 2026](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BEXTERNAL-PUBLIC%5D%20Roof%20Open-Source%20Development%20Public%20Roadmap%20Timeline_2026_DataFoundationLab.png)

[Read the full 2026 public development roadmap (PDF)](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BEXTERNAL-PUBLIC%5D%20Roof%20Open-Source%20Development%20Public%20Roadmap_2026_DataFoundationLab.pdf).

## Documentation

### Project Specifications

Current project specifications describe the project direction and component designs; the implementation and limitations of this preview are documented above.

- [Roof Project Root Specification (PDF)](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BINTERNAL-EXTERNAL-PUBLIC%5D%20Roof%20Project%20Root%20Specification_DataFoundationLab_2024-PRESENT.pdf)
- [Roof Engine Project Specification (PDF)](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BINTERNAL-EXTERNAL-PUBLIC%5D%20Roof%20Engine%20Project%20Specification_DataFoundationLab_2024-PRESENT.pdf)
- [Roof PathFinder Project Specification (PDF)](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BINTERNAL-EXTERNAL-PUBLIC%5D%20Roof%20PathFinder%20Project%20Specification_DataFoundationLab_2024-PRESENT.pdf)
- [Roof Internal Library Project Specification (PDF)](PROJECT_RUNNING_DOCUMENTS/CURRENT/%5BINTERNAL-EXTERNAL-PUBLIC%5D%20Roof%20Internal%20Library%20Project%20Specification_DataFoundationLab_2024-PRESENT.pdf)

### Repository Guides

- [CONTRIBUTING.md](CONTRIBUTING.md): contribution expectations, licensing, validation and review.
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md): community standards, private reporting and enforcement.
- [SECURITY.md](SECURITY.md): supported-version policy, private vulnerability reporting and coordinated disclosure for Roof.
- [MAINTAINERS.md](MAINTAINERS.md): accountable responsibilities, public issue labels and reporting setup/verification steps.
- [BUILDING.md](BUILDING.md): component builds and NativeAOT workflow.
- [DEVELOPMENT.md](DEVELOPMENT.md): development procedures, configuration behavior and privileged execution boundaries.
- [DISTRIBUTION.md](DISTRIBUTION.md): distribution model and future release procedures.
- [LICENSING.md](LICENSING.md), [LICENSES/](LICENSES/) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md): component terms and third-party attribution.

Roof.Engine is GPL-3.0-only; Roof.PathFinder is AGPL-3.0-only; Roof.Client, Roof.Common and Roof.NativeAppSDK are LGPL-3.0-only; VMTesting is Apache-2.0. The current licensing document lists ClientTestConsole as N/A; its presence is not a blanket license grant. No single repository-wide license replaces the component allocations. See the licensing documents for combined-work and NativeAOT rebuilding details.
