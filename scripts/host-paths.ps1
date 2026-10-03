# Shared discovery for setup, install, verify and uninstall. Never guess drive letters.
function ConvertTo-NavisworksDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    $value = [Environment]::ExpandEnvironmentVariables($Path.Trim().Trim('"'))
    try {
        if ([IO.Path]::GetExtension($value) -eq '.lnk' -and (Test-Path -LiteralPath $value -PathType Leaf)) {
            $shell = New-Object -ComObject WScript.Shell
            try { $value = $shell.CreateShortcut($value).TargetPath }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
        }
        if ([IO.Path]::GetFileName($value) -ieq 'Roamer.exe') { $value = Split-Path -Parent $value }
        if (-not $value) { return $null }
        return [IO.Path]::GetFullPath($value).TrimEnd('\')
    } catch { return $null }
}

function Get-Navisworks2023HostError([string]$Path) {
    $directory = ConvertTo-NavisworksDirectory $Path
    if (-not $directory) { return '请选择 Navisworks Manage 2023 安装目录或 Roamer.exe。' }
    $exe = Join-Path $directory 'Roamer.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        return '目录中没有 Roamer.exe；Exporters 和插件解压目录不能作为安装目标。'
    }
    $product = (Get-Item -LiteralPath $exe).VersionInfo.ProductName
    # Real Manage Roamer.exe uses the generic ProductName "Navisworks".
    if ($product -match 'Simulate|Freedom' -or (Split-Path -Leaf $directory) -match 'Navisworks.*(Simulate|Freedom)') {
        return "仅支持 Navisworks Manage 2023，当前产品：$product。"
    }
    foreach ($name in @('Autodesk.Navisworks.Api', 'Autodesk.Navisworks.ComApi', 'Autodesk.Navisworks.Interop.ComApi')) {
        $file = Join-Path $directory "$name.dll"
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return "安装不完整，缺少 $name.dll。" }
        try {
            $assembly = [Reflection.AssemblyName]::GetAssemblyName($file)
            if ($assembly.Name -ne $name -or $assembly.Version.Major -ne 20) {
                return "仅支持 Navisworks Manage 2023（API 20.x），$name 的版本不匹配：$($assembly.Version)。"
            }
        } catch { return "无法读取 $name.dll：$($_.Exception.Message)" }
    }
    return $null
}

function Get-NavisworksPathCacheFile {
    return (Join-Path $env:LOCALAPPDATA 'JiePinPai\Navisworks2023\installation.json')
}

function Save-Navisworks2023Path([string]$Path) {
    $cacheFile = Get-NavisworksPathCacheFile
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $cacheFile))
    [IO.File]::WriteAllText($cacheFile, (@{ path = (ConvertTo-NavisworksDirectory $Path) } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}

function Get-NavisworksPathCandidates {
    [Environment]::GetEnvironmentVariable('NAVISWORKS_2023_PATH')
    $cacheFile = Get-NavisworksPathCacheFile
    if (Test-Path -LiteralPath $cacheFile -PathType Leaf) {
        try { (Get-Content -LiteralPath $cacheFile -Raw -Encoding UTF8 | ConvertFrom-Json).path } catch { }
    }
    foreach ($process in @(Get-Process -Name Roamer -ErrorAction SilentlyContinue)) {
        try { $process.Path } catch { }
    }
    # Read both registry views even when a 32-bit shell launches this installer.
    foreach ($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryHive]::CurrentUser)) {
        foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
            $base = $null
            try {
                $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, $view)
                foreach ($keyName in @('SOFTWARE\Autodesk\Navisworks Manage\20.0', 'SOFTWARE\Autodesk\Navisworks Manage\20.0\Location', 'SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\Roamer.exe')) {
                    $key = $base.OpenSubKey($keyName)
                    if ($key) {
                        try { foreach ($valueName in @('', 'Path', 'InstallPath', 'InstallDir', 'InstallLocation')) { $key.GetValue($valueName) } }
                        finally { $key.Dispose() }
                    }
                }
                $uninstall = $base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
                if ($uninstall) {
                    try {
                        foreach ($child in $uninstall.GetSubKeyNames()) {
                            $key = $uninstall.OpenSubKey($child)
                            if (-not $key) { continue }
                            try {
                                $name = [string]$key.GetValue('DisplayName')
                                if ($name -notmatch 'Navisworks.*Manage.*2023' -or $name -match 'Exporters') { continue }
                                $key.GetValue('InstallLocation')
                                $icon = [string]$key.GetValue('DisplayIcon')
                                if ($icon -match '^\s*"?(.+?Roamer\.exe)"?(?:,\s*-?\d+)?\s*$') { $Matches[1] }
                            } finally { $key.Dispose() }
                        }
                    } finally { $uninstall.Dispose() }
                }
            } catch { Write-Verbose "Cannot read $hive/$view registry: $_" }
            finally { if ($base) { $base.Dispose() } }
        }
    }
    foreach ($folder in @('DesktopDirectory', 'CommonDesktopDirectory', 'StartMenu', 'CommonStartMenu')) {
        $directory = [Environment]::GetFolderPath($folder)
        if (-not $directory -or -not (Test-Path -LiteralPath $directory)) { continue }
        Get-ChildItem -LiteralPath $directory -Filter '*.lnk' -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.BaseName -match 'Navisworks|Roamer' } | ForEach-Object { $_.FullName }
    }
    foreach ($root in @($env:ProgramW6432, $env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ } | Select-Object -Unique) {
        Join-Path $root 'Autodesk\Navisworks Manage 2023'
    }
}

function Find-Navisworks2023Paths {
    $seen = @{}
    foreach ($candidate in @(Get-NavisworksPathCandidates)) {
        $directory = ConvertTo-NavisworksDirectory $candidate
        if (-not $directory -or $seen.ContainsKey($directory)) { continue }
        $seen[$directory] = $true
        if (-not (Get-Navisworks2023HostError $directory)) { $directory }
    }
}

function Resolve-Navisworks2023Path([string]$Path) {
    if ($Path) {
        $errorMessage = Get-Navisworks2023HostError $Path
        if ($errorMessage) { throw $errorMessage }
        return (ConvertTo-NavisworksDirectory $Path)
    }
    $paths = @(Find-Navisworks2023Paths)
    if ($paths.Count -eq 1) { return $paths[0] }
    if ($paths.Count -gt 1) { throw "检测到多个 2023 安装，请用安装窗口选择，或传入 -NavisworksPath：$($paths -join '；')" }
    throw '未找到 Navisworks Manage 2023。请在安装窗口选择 Roamer.exe，或传入 -NavisworksPath。'
}
