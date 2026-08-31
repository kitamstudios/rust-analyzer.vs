# Prerequisites for rust-analyzer.vs

rust-analyzer.vs supports Windows amd64 with Visual Studio Community, Professional, or Enterprise:

- Visual Studio 2022 17.12 or later within 17.x
- Visual Studio 2026 18.x

The extension requires a complete Core Editor installation in `[17.12,19.0)`, `rustup` on the
Visual Studio process `PATH`, a default Rust toolchain, and operational Cargo. Nightly is optional
and needed only for Test Explorer.

> ❗ **Important**
>
> Run only the Visual Studio commands for your edition. After installing or updating any Visual
> Studio or Rust prerequisite, changing the default Rust toolchain, or changing `PATH`, close every
> Visual Studio process and start a fresh one.

## Install or upgrade Visual Studio

Run the install command when the edition is absent. Run the upgrade command for an existing
installation.

### Visual Studio 2022

#### Community

```powershell
# Install
winget install --exact --id Microsoft.VisualStudio.2022.Community --source winget
# Upgrade
winget upgrade --exact --id Microsoft.VisualStudio.2022.Community --source winget
```

#### Professional

```powershell
# Install
winget install --exact --id Microsoft.VisualStudio.2022.Professional --source winget
# Upgrade
winget upgrade --exact --id Microsoft.VisualStudio.2022.Professional --source winget
```

#### Enterprise

```powershell
# Install
winget install --exact --id Microsoft.VisualStudio.2022.Enterprise --source winget
# Upgrade
winget upgrade --exact --id Microsoft.VisualStudio.2022.Enterprise --source winget
```

### Visual Studio 2026

The current Visual Studio 2026 WinGet IDs are not year-qualified.

#### Community

```powershell
# Install
winget install --exact --id Microsoft.VisualStudio.Community --source winget
# Upgrade
winget upgrade --exact --id Microsoft.VisualStudio.Community --source winget
```

#### Professional

```powershell
# Install
winget install --exact --id Microsoft.VisualStudio.Professional --source winget
# Upgrade
winget upgrade --exact --id Microsoft.VisualStudio.Professional --source winget
```

#### Enterprise

```powershell
# Install
winget install --exact --id Microsoft.VisualStudio.Enterprise --source winget
# Upgrade
winget upgrade --exact --id Microsoft.VisualStudio.Enterprise --source winget
```

### Verify Visual Studio

Visual Studio installs `vswhere` with its installer. By default, `vswhere` excludes incomplete or
unlaunchable instances. This command additionally requires the Core Editor component and the
supported version range:

```powershell
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'vswhere.exe was not found. Repair the Visual Studio Installer.'
}

$instances = @(& $vswhere -products * -version '[17.12,19.0)' -requires Microsoft.VisualStudio.Component.CoreEditor -format json | ConvertFrom-Json)
if ($instances.Count -eq 0) {
    throw 'No complete Visual Studio installation with Core Editor was found in [17.12,19.0).'
}

$instances | Select-Object displayName, installationVersion, installationPath, isComplete
```

## Install Rust with stable as the default

Use this only when `rustup` is not installed. It downloads the official Windows amd64 MSVC installer
and its published SHA-256 checksum over HTTPS, verifies the download, installs stable as the default
toolchain, and removes the installer.

```powershell
$rustupUri = 'https://static.rust-lang.org/rustup/dist/x86_64-pc-windows-msvc/rustup-init.exe'
$rustupInstaller = Join-Path ([IO.Path]::GetTempPath()) "rustup-init-$([Guid]::NewGuid()).exe"

try {
    $expectedHash = ((Invoke-RestMethod -Uri "$rustupUri.sha256").Trim() -split '\s+')[0]
    if ($expectedHash -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'The rustup SHA-256 response was invalid.'
    }

    Invoke-WebRequest -Uri $rustupUri -OutFile $rustupInstaller
    $actualHash = (Get-FileHash -LiteralPath $rustupInstaller -Algorithm SHA256).Hash
    if ($actualHash -ine $expectedHash) {
        throw 'rustup-init.exe failed SHA-256 verification.'
    }

    & $rustupInstaller -y --default-toolchain stable
    if ($LASTEXITCODE -ne 0) {
        throw "rustup-init.exe failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $rustupInstaller -Force -ErrorAction SilentlyContinue
}
```

## Update an existing rustup installation

```powershell
rustup self update
rustup update stable
rustup default stable
```

## Add Cargo tools to PATH when absent

`rustup` normally adds `%USERPROFILE%\.cargo\bin`. This keeps existing entries, adds the directory
to the user `PATH` only when absent, and also updates the current PowerShell process:

```powershell
$cargoBin = Join-Path $env:USERPROFILE '.cargo\bin'
$userPath = [Environment]::GetEnvironmentVariable('Path', [EnvironmentVariableTarget]::User)
$userPathEntries = @($userPath -split ';' | Where-Object { $_ })
$userHasCargoBin = $userPathEntries | Where-Object {
    [Environment]::ExpandEnvironmentVariables($_).TrimEnd('\') -ieq $cargoBin.TrimEnd('\')
}

if (-not $userHasCargoBin) {
    [Environment]::SetEnvironmentVariable(
        'Path',
        (@($userPathEntries + $cargoBin) -join ';'),
        [EnvironmentVariableTarget]::User)
}

$processHasCargoBin = $env:Path -split ';' | Where-Object {
    $_.TrimEnd('\') -ieq $cargoBin.TrimEnd('\')
}

if (-not $processHasCargoBin) {
    $env:Path = "$cargoBin;$env:Path"
}
```

Close every Visual Studio process and start a fresh one after changing `PATH`.

## Verify Rust and Cargo

Run this outside a directory with a Rust toolchain override when checking the stable default:

```powershell
Get-Command rustup, rustc, cargo | Select-Object Name, Source
rustup --version
rustup default
rustup show active-toolchain
rustc --version --verbose
cargo --version
```

## Optional nightly toolchain for Test Explorer

Nightly is not a startup prerequisite. Install it for Test Explorer, or update an existing nightly:

```powershell
# Install
rustup toolchain install nightly
# Upgrade
rustup update nightly
```

At the Cargo workspace root, replace the example path and set a directory-local override:

```powershell
Set-Location 'C:\path\to\cargo-workspace'
rustup override set nightly
rustup show active-toolchain
```

## Official sources

- Microsoft: [WinGet install](https://learn.microsoft.com/windows/package-manager/winget/install),
  [WinGet upgrade](https://learn.microsoft.com/windows/package-manager/winget/upgrade), and
  [Visual Studio command-line installation](https://learn.microsoft.com/visualstudio/install/use-command-line-parameters-to-install-visual-studio?view=visualstudio)
- Microsoft WinGet manifests: Visual Studio 2022
  [Community](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VisualStudio/2022/Community),
  [Professional](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VisualStudio/2022/Professional),
  and
  [Enterprise](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VisualStudio/2022/Enterprise);
  Visual Studio 2026
  [Community](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VisualStudio/Community),
  [Professional](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VisualStudio/Professional),
  and
  [Enterprise](https://github.com/microsoft/winget-pkgs/tree/master/manifests/m/Microsoft/VisualStudio/Enterprise)
- Microsoft: [`vswhere`](https://github.com/microsoft/vswhere),
  [Visual Studio workload and component IDs](https://learn.microsoft.com/visualstudio/install/workload-and-component-ids?view=visualstudio),
  and the
  [Visual Studio extension compatibility model](https://learn.microsoft.com/visualstudio/extensibility/migration/extension-compatibility?view=visualstudio)
- Rust: [Install Rust](https://www.rust-lang.org/tools/install),
  [manual rustup installation and checksums](https://rust-lang.github.io/rustup/installation/other.html),
  [rustup updates](https://rust-lang.github.io/rustup/basics.html), and
  [toolchain overrides](https://rust-lang.github.io/rustup/overrides.html)
- Cargo: [installation](https://doc.rust-lang.org/cargo/getting-started/installation.html)
