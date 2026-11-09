# Maintenance and Community Administration

## Scope

This document defines responsibilities, public issue labels and reporting administration procedures for [Roof](https://github.com/Data-Foundation-Lab/Roof). It accompanies [CONTRIBUTING.md](CONTRIBUTING.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) and [SECURITY.md](SECURITY.md).

## Accountable Maintainer

| Responsibility | Accountable person and procedure |
| --- | --- |
| Public issue triage and label maintenance | [Steve Yoo (@dflbsteve)](https://github.com/dflbsteve) (DFLab Maintainer): confirm scope, request missing information, assign categories and explain closure or deferral. |
| Contribution review and merge approval | Steve: check correctness, validation, compatibility, licensing and maintenance impact; obtain one eligible approving review and satisfy active rules. Authors cannot approve their own work. |
| Sensitive or high-impact review | Steve: explicitly assess security boundaries, protocol/persistence changes, architecture, licensing and release implications; obtain specialist review when needed. |
| Security intake, remediation and disclosure | Steve: monitor the private channels, control access, coordinate fixes/reporters/upstream parties and approve public disclosure under SECURITY.md. |
| Conduct reports and enforcement | Steve, subject to recusal: apply CODE_OF_CONDUCT.md. DFLab appoints an uninvolved reviewer for conflicts and appeals. |
| Release and maintenance decisions | Steve: confirm source/release traceability, validation, notices and distribution obligations; communicate upgrade requirements and material support changes. |
| Policy and configuration maintenance | Steve: keep these documents consistent with actual repository settings, periodically check reporting access/delivery, and update ownership and support information when they change. |

Steve coordinates maintenance coverage with DFLab. DFLab appoints an uninvolved reviewer for conduct complaints involving a conflict of interest and for appeals. If no eligible contribution reviewer is available, routine changes wait; necessary emergency exceptions must be authorised, recorded and retrospectively reviewed under DEVELOPMENT.md, consistently with GitHub permissions.

## Public Issue Labels

| Label | Meaning |
| --- | --- |
| `type: bug` | Existing behaviour fails its documented or intended requirements. |
| `type: feature` | New behaviour or an enhancement requiring a scope decision. |
| `type: documentation` | User, developer or policy documentation. |
| `type: testing` | Meaningful tests or validation infrastructure. |
| `type: build` | Build tooling, packaging, CI or dependency maintenance. |
| `type: refactor` | Internal restructuring without an intended behaviour change. |
| `type: accessibility` | Accessibility barriers or improvements. |
| `type: maintenance` | Other repository upkeep and maintenance. |
| `status: needs-triage` | Awaiting maintainer assessment. |
| `status: needs-information` | Maintainer has requested specific information. |
| `status: accepted` | Within scope; acceptance does not promise implementation or a date. |
| `status: blocked` | A documented public dependency prevents progress. |
| `priority: high` | Steve judges that prompt attention is warranted. |
| `priority: normal` | Normal maintenance prioritisation. |
| `priority: low` | Useful but lower urgency. |
| `good first issue` | Bounded, documented work suitable for a newcomer. |
| `help wanted` | Maintainer welcomes external implementation or investigation. |
| `duplicate` | Closed in favour of a linked public issue. |
| `invalid` | Not an actionable issue for this project; explain why. |
| `wontfix` | Declined or outside current maintenance scope; explain why. |
| `security: disclosed` | A publicly disclosed security issue with an approved public advisory reference. |

Use `component: engine`, `component: pathfinder`, `component: client`, `component: common`, `component: native-sdk` and `component: vmtesting`.

Normally assign one primary type, one current status and at most one priority; use multiple components when needed. Steve owns priority and acceptance decisions. Replace obsolete status labels as work progresses; issue closure records completion or another resolution. Labels do not replace a review or promise a fix. Do not use a public label, placeholder issue or cross-link to track an embargoed finding. `security: disclosed` is only for information already approved for publication; suspected vulnerabilities go privately to SECURITY.md.

## Ongoing Maintenance

Steve maintains the label definitions and repository protections, verifies reporting channels after access or routing changes, and keeps third-party notices and release compliance guidance current. Policy changes follow the normal contribution review process. Review the documentation when maintainers, supported versions, reporting routes, infrastructure or contribution practices change.
