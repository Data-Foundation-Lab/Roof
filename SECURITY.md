# Security Policy (Roof)

## Scope and Supported Versions

This policy covers [Roof](https://github.com/Data-Foundation-Lab/Roof) and its published components and development tooling. [Steve Yoo (@dflbsteve)](https://github.com/dflbsteve) (DFLab Maintainer) owns security triage, remediation coordination and disclosure approval for Roof.

| Version or source | Security-report and maintenance policy |
| --- | --- |
| Developer Preview 0.1.0, the initial public source baseline | Reports accepted; confirmed issues assessed for remediation. |
| Every subsequent official Roof version from that baseline onward | Reports accepted, including reports affecting older official versions. |
| Unreleased development revisions | Reports accepted; include the repository and exact commit. |
| Pre-baseline internal versions, third-party forks or modified deployments | Provide details; impact on official versions is assessed, but maintenance of those variants is not promised. |

All official versions from the baseline are in scope for security assessment. This does not promise a separate patch for every historical version: remediation may require updating to a later version, and advisories must identify affected versions, fixed revisions and any available mitigations. No end-of-support date has been established. Any future support change must be documented explicitly.

The initial preview is experimental and source-only. Acceptance of security reports is not a production-readiness guarantee, a deployment support commitment or a claim that the implementation has undergone a security audit. Internal assembly versions and IA/BETA/RELEASE build configurations are not separate public support guarantees. See [README.md](README.md) and [DEVELOPMENT.md](DEVELOPMENT.md).

## Report Privately

Do not disclose suspected vulnerabilities in public issues, discussions, pull requests or comments. Use [Roof’s GitHub private reporting form](https://github.com/Data-Foundation-Lab/Roof/security/advisories/new) when available.

If the form is unavailable, you cannot use GitHub, or you do not receive an acknowledgement, email [roof-security@dflb.org](mailto:roof-security@dflb.org). Use a neutral subject such as `Private Security Report: Roof`. Identify the affected components and avoid duplicating sensitive evidence unnecessarily.

If neither private route is available, do not post sensitive details publicly.

Include, where available:

- Repository, component, version or commit, platform and build configuration.
- A description of the problem, expected security boundary and potential impact.
- Minimal reproduction steps or a proof of concept using synthetic data in an isolated environment.
- Any affected configurations, prerequisites, suggested mitigation and related upstream reports.
- A contact method, credit preference and any timing constraints.

Do not send real credentials, private keys, user files, unnecessary personal data or bulk production logs. Start with a minimal report and arrange restricted transfer for sensitive evidence. No project encryption key or end-to-end encrypted email mechanism is currently published; ordinary email should not be treated as end-to-end encrypted.

## Response and Disclosure

Steve is responsible for acknowledging, assessing and tracking reports and for communicating with reporters. Working targets are acknowledgement within three business days, an initial assessment within ten business days, and a progress update at least every fourteen calendar days while an accepted report remains unresolved. These are best-effort targets for a maintainer-led project, not service guarantees. Follow up privately if a target is missed.

The response process is:

1. Establish affected components, revisions, severity and exposure; reproduce only in isolated environments and identify immediate mitigations.
2. Retain evidence and develop fixes privately while disclosure could expose users. Invite only necessary reviewers and coordinate cross-project or upstream dependencies privately.
3. Validate the fix and regression risk, identify affected and fixed revisions, and prepare safe upgrade or mitigation instructions. Distinguish a source fix from a binary release; do not imply installers or binaries exist when they do not.
4. Agree a disclosure plan with the reporter and affected parties. Use 90 calendar days from receipt as an initial planning target, adjusted case by case for severity, active exploitation, fix readiness and dependencies. Document revised dates and reasons privately, and keep the reporter informed. The target is neither an automatic publication date nor a request for indefinite secrecy.
5. Steve approves disclosure and publication of the advisory, available fixes and necessary public references. Active exploitation or urgent user protection may justify an earlier warning with limited technical detail. Consider a CVE through an appropriate assignment channel when warranted; assignment is not guaranteed.
6. Credit reporters according to their consent, including anonymity, and publish only the evidence necessary to explain impact and remediation. Remove secrets, personal data and confidential infrastructure details. Confirm public links and follow up on incomplete remediation.

This policy requests coordinated disclosure; it does not bind reporters to an unagreed embargo. Timing disagreements should be discussed privately and recorded.

## Private Handling and Retention

Steve owns the restricted security record. Use GitHub private security reports/advisories or another access-controlled DFLab security record for reproduction material, impact analysis, exploit details, credentials and embargoed patches. Do not create a public issue merely to track an undisclosed finding; even titles, labels, commit messages or links can reveal it.

Grant access only to people needed for triage, remediation or disclosure and review access when participants change. Use private review and development facilities for embargoed fixes, including build logs and artifacts; a public draft pull request or ordinary public branch is not private. If a report is accidentally public, restrict further dissemination, notify Steve privately and rotate exposed secrets where necessary. Editing or deleting a public message cannot guarantee removal from copies or history.

Disclosure does not make all underlying evidence public. Keep sensitive material private unless Steve approves a specific sanitised publication. Preserve a minimal record of decisions, affected/fixed revisions, validation and disclosure. Review retention at closure and periodically thereafter, deleting unnecessary personal data and raw evidence when no longer needed, subject to applicable obligations.

## Research Boundaries

Use your own isolated test systems and synthetic data, following the host-safety guidance. Do not test against DFLab infrastructure or other people's devices/data without their explicit permission. This policy does not grant testing authorisation, legal safe harbour or a paid bounty; none is established here.
