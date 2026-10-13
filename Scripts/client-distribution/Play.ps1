param(
    [string]$Login,
    [string]$User,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
try {
    $config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'server.json') -Raw | ConvertFrom-Json
    if (-not $Login) { $Login = [string]$config.login }
    if ($Login -notmatch '^[a-zA-Z0-9.\[\]:_-]+$') { throw 'Enter a valid login server host:port in server.json.' }
    if (-not $User) { $User = Read-Host 'Account name' }
    if ([string]::IsNullOrWhiteSpace($User)) { throw 'An account name is required.' }
    $launchArgs = @(
        '--client', (Join-Path $PSScriptRoot 'bin32\archeage.exe'),
        '--driver-dll', (Join-Path $PSScriptRoot 'aadriver.dll'),
        '--pak-manifest', (Join-Path $PSScriptRoot 'game.aacm'),
        '--login', $Login, '--user', $User, '--lang', [string]$config.language,
        '--sound', '--fast-start', '--fast-pak', '--smooth-start'
    )
    if ($DryRun) {
        & (Join-Path $PSScriptRoot 'aaemu-launcher.exe') @launchArgs --dry-run
        if ($LASTEXITCODE -ne 0) { throw "Launcher exited with code $LASTEXITCODE" }
        exit 0
    }
    $secret = Read-Host 'Password' -AsSecureString
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $previousPassword = $env:AAEMU_PASSWORD
    try {
        $env:AAEMU_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
        if ([string]::IsNullOrEmpty($env:AAEMU_PASSWORD)) { throw 'A password is required.' }
        & (Join-Path $PSScriptRoot 'aaemu-launcher.exe') @launchArgs
        if ($LASTEXITCODE -ne 0) { throw "Launcher exited with code $LASTEXITCODE" }
    }
    finally {
        $env:AAEMU_PASSWORD = $previousPassword
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer)
        $secret.Dispose()
    }
}
catch {
    Write-Error $_
    exit 1
}
