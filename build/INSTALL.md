# Installing Caffeine (MSIX)

Caffeine ships as a signed **MSIX** package. Because it's signed with a
**self-signed** certificate (fine for personal use), each machine must trust
that certificate once before Windows will install the app.

## First-time machine setup (once per PC)

1. **Trust the certificate.** In an **elevated** PowerShell (Run as
   administrator), from the repo root:

   ```powershell
   Import-Certificate -FilePath build\CaffeineDev.cer `
       -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

   (Or double-click `build\CaffeineDev.cer` → **Install Certificate** →
   **Local Machine** → **Place all certificates in the following store** →
   **Trusted People**.)

## Install the app

Double-click the `.msix` and click **Install**, or from PowerShell:

```powershell
Add-AppxPackage -Path `
  src\Caffeine.App\AppPackages\Caffeine.App_1.0.0.0_x64_Test\Caffeine.App_1.0.0.0_x64.msix
```

Caffeine then appears in the Start Menu like any installed app.

## Updating

Bump `Version` in `src\Caffeine.App\Package.appxmanifest` (e.g. `1.0.1.0`),
re-run the packaging script, then `Add-AppxPackage` the new `.msix` — it
upgrades in place. No need to re-trust the certificate.

## Uninstalling

Settings → Apps → Caffeine → Uninstall, or:

```powershell
Get-AppxPackage -Name Caffeine.App | Remove-AppxPackage
```

---

## Rebuilding the package

```powershell
# One-time, per dev machine (the .pfx is gitignored):
build\make-cert.ps1

# Every time you want a fresh installable package:
build\package.ps1              # x64 (default)
build\package.ps1 -Platform ARM64
```

The signed `.msix` is written under
`src\Caffeine.App\AppPackages\Caffeine.App_<version>_<platform>_Test\`.
