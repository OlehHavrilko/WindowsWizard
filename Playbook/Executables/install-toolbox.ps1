param([Parameter(Mandatory)][string]$Source)
$ErrorActionPreference = 'Stop'
$dst = Join-Path $env:ProgramFiles 'Bare OS'
New-Item -ItemType Directory -Force -Path $dst | Out-Null
foreach ($f in 'bareos.exe', 'BareOS-Toolbox.exe') {
    Copy-Item -LiteralPath (Join-Path $Source $f) -Destination $dst -Force
}
$cat = Join-Path $dst 'catalog'
if (Test-Path $cat) { Remove-Item -LiteralPath $cat -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $Source 'catalog') -Destination $cat -Recurse -Force

$exe = Join-Path $dst 'BareOS-Toolbox.exe'
$sh = New-Object -ComObject WScript.Shell
$places = @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'),
    (Join-Path $env:PUBLIC 'Desktop')
)
foreach ($p in $places) {
    New-Item -ItemType Directory -Force -Path $p | Out-Null
    $lnk = $sh.CreateShortcut((Join-Path $p 'Bare OS Toolbox.lnk'))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $dst
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'Bare OS Toolbox'
    $lnk.Save()
}
Write-Output "Bare OS Toolbox установлен: $dst"
