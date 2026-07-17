# Installing Caffeine (MSIX)

Caffeine ships as a signed **MSIX** package. Because it's signed with a
**self-signed** certificate (fine for personal use), each machine must trust
that certificate once before Windows will install the app.

## Get the package

Every push to `master` automatically builds, signs, and publishes a new
release. Go to the repo's **[Releases page](../../releases)** and download,
from the latest release:

- `Caffeine.App_<version>_x64.msix`
- `CaffeineDev.cer` (only needed the first time you set up a machine)

## First-time machine setup (once per PC)

1. **Trust the certificate.** In an **elevated** PowerShell (Run as
   administrator), from wherever you downloaded `CaffeineDev.cer`:

   ```powershell
   Import-Certificate -FilePath CaffeineDev.cer `
       -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

   (Or double-click `CaffeineDev.cer` → **Install Certificate** →
   **Local Machine** → **Place all certificates in the following store** →
   **Trusted People**.)

## Install the app

Double-click the downloaded `.msix` and click **Install**, or from
PowerShell:

```powershell
Add-AppxPackage -Path .\Caffeine.App_<version>_x64.msix
```

Caffeine then appears in the Start Menu like any installed app.

## Updating

Download the newer `.msix` from the Releases page and `Add-AppxPackage` it —
it upgrades in place. No need to re-trust the certificate.

## Uninstalling

Settings → Apps → Caffeine → Uninstall, or:

```powershell
Get-AppxPackage -Name Caffeine.App | Remove-AppxPackage
```

---

## Building it yourself (local development)

If you're working on Caffeine and want a local build instead of downloading
a release:

```powershell
# One-time, per dev machine (the .pfx is gitignored):
build\make-cert.ps1

# Every time you want a fresh installable package:
build\package.ps1              # x64 (default)
build\package.ps1 -Platform ARM64
```

The signed `.msix` is written under
`src\Caffeine.App\AppPackages\Caffeine.App_<version>_<platform>_Test\`.
