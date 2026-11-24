# Windows code signing

FrameInk's GitHub releases are currently unsigned. An icon, a checksum and an HTTPS download do not give a Windows executable a verified publisher. [Let's Encrypt issues TLS certificates](https://letsencrypt.org/docs/faq/), not Windows Authenticode code-signing certificates.

## Getting a certificate

- **SignPath Foundation:** [Check eligibility](https://signpath.org/terms), then [apply for free managed signing](https://signpath.org/apply) for the MIT-licensed project. Approval is not automatic. SignPath Foundation is the visible publisher, and its terms require manual approval for each release signature.
- **Sign as yourself or your organisation:** Buy an Authenticode code-signing certificate from a [supported certificate authority](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) that can verify your identity and country. For GitHub Actions, prefer a provider that can sign using a cloud-protected key. A USB token needs a trusted runner with access to that device.
- **Azure Artifact Signing:** [Check eligibility first](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart). Public Trust for individual developers is limited to the US and Canada. Private Trust and self-signed certificates are for managed or test machines, not public GitHub downloads.

After approval or issuance, use the provider's signing service to timestamp and sign the FrameInk executable **before** creating the ZIP and checksum. Keep private keys and signing credentials outside the repository. A valid signature identifies the publisher but does not guarantee an immediate SmartScreen reputation.
