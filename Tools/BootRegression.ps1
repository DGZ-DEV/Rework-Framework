<#
.SYNOPSIS
  Test de regresion del boot de Rework Reforjed.

.DESCRIPTION
  Verifica que el arranque del juego con Rework produce los marcadores sanos
  esperados en Player.log. Analiza un log existente por defecto, o lanza el
  juego y espera a los marcadores con -Launch.

  Exit code: 0 = PASS, 1 = FAIL, 2 = error de entorno/timeout.
#>
[CmdletBinding()]
param(
    [switch]$Launch,
    [string]$LogPath = "$env:USERPROFILE\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log",
    [string]$GameExe = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64.exe",
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]

function Check([bool]$ok, [string]$name, [string]$detail) {
    if ($ok) {
        Write-Host ("  [PASS] " + $name) -ForegroundColor Green
    } else {
        $msg = "  [FAIL] " + $name
        if ($detail) { $msg = $msg + " -> " + $detail }
        Write-Host $msg -ForegroundColor Red
        $script:failures.Add($name)
    }
}

# Contadores minimos esperados en la linea del escaner (ajustar al cambiar contenido).
$ExpectedCounters = [ordered]@{
    'AI'           = 1
    'Gizmo'        = 1
    'Inspect'      = 1
    'Alert'        = 1
    'Tab'          = 1
    'Schedule'     = 4
    'Migration'    = 1
    'Compat'       = 1
    'DefBuilder'   = 1
    'Quest'        = 1
    'Lore'         = 1
    'StatusEffect' = 1
    'Overlay'      = 1
}

Write-Host "== Rework Boot Regression ==" -ForegroundColor Cyan

# Fase 0 (opcional): lanzar el juego y esperar al escaner.
if ($Launch) {
    if (-not (Test-Path $GameExe)) {
        Write-Error ("No se encontro el ejecutable: " + $GameExe)
        exit 2
    }
    if (Test-Path $LogPath) {
        Copy-Item $LogPath ($LogPath + ".bootreg.bak") -Force
    }
    Write-Host "Lanzando el juego (espera maxima: $TimeoutSeconds s)..."
    Start-Process $GameExe
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $found = $false
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $LogPath) {
            if (Select-String -Path $LogPath -Pattern 'ReworkAttributeScanner.*Escaneo completo' -Quiet) { $found = $true; break }
        }
        Start-Sleep -Seconds 3
    }
    if (-not $found) {
        Write-Host "TIMEOUT: el escaner no aparecio." -ForegroundColor Red
        exit 2
    }
    Start-Sleep -Seconds 5
}

# Fase 1: comprobaciones sobre el log.
if (-not (Test-Path $LogPath)) { Write-Error ("No existe: " + $LogPath); exit 2 }
$kb = '{0:N1}' -f ((Get-Item $LogPath).Length / 1KB)
Write-Host ("Analizando: " + $LogPath + " (" + $kb + " KB)")
$log = Get-Content $LogPath -Encoding UTF8

Write-Host ""
Write-Host "-- Marcadores estructurales --" -ForegroundColor Cyan
Check ((($log | Select-String 'layoutOK=True').Count) -ge 1) "Entorno seguro para reescritura" ""
Check ((($log | Select-String 'VERIFICACI.N OK').Count) -ge 1) "Rewrite en efecto (VERIFICACION OK)" ""

Write-Host ""
Write-Host "-- Linea del escaner --" -ForegroundColor Cyan
$scannerLine = ($log | Select-String 'ReworkAttributeScanner.*Escaneo completo' | Select-Object -Last 1)
if (-not $scannerLine) {
    Check $false "Linea del escaner presente" "no se encontro en el log"
} else {
    Check $true "Linea del escaner presente" ""
    Write-Host ("       " + $scannerLine.Line.Trim()) -ForegroundColor DarkGray
    foreach ($name in $ExpectedCounters.Keys) {
        $min = $ExpectedCounters[$name]
        $m = [regex]::Match($scannerLine.Line, ($name + "=([0-9]+)"))
        if (-not $m.Success) {
            Check $false ("Contador " + $name) "ausente en la linea del escaner"
        } else {
            $num = [int]$m.Groups[1].Value
            $d = ""
            if ($num -lt $min) { $d = ("obtenido: " + $num) }
            Check ($num -ge $min) ("Contador " + $name + " >= " + $min) $d
        }
    }
}

Write-Host ""
Write-Host "-- Registros de contenido --" -ForegroundColor Cyan
Check ((($log | Select-String 'ReworkJobRegistry:.*registrado').Count) -ge 1) "ReworkJobRegistry registro trabajos" ""
Check ((($log | Select-String 'ReworkWorkGiverRegistry:.*registrado').Count) -ge 1) "ReworkWorkGiverRegistry registro WorkGivers" ""
Check ((($log | Select-String 'ReworkAlert.*conectada').Count) -ge 1) "Alertas declarativas conectadas" ""

Write-Host ""
Write-Host "-- Self-check de contenido --" -ForegroundColor Cyan
$selfCheck = ($log | Select-String 'ReworkContent Verify' | Select-Object -Last 1)
if ($selfCheck) {
    Check ($selfCheck.Line -match 'VERIFICADO') "ReworkContent verificado" ""
} else {
    Write-Host "  [SKIP] ReworkContent Verify no aparece en este log" -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "-- Excepciones --" -ForegroundColor Cyan
$exceptions = @($log | Select-String 'Exception')
Check ($exceptions.Count -eq 0) "0 lineas con Exception" ($exceptions.Count.ToString() + " encontradas")
if ($exceptions.Count -gt 0) {
    $exceptions | Select-Object -First 5 | ForEach-Object {
        Write-Host ("       L" + $_.LineNumber + ": " + $_.Line.Trim()) -ForegroundColor DarkRed
    }
}

Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "BOOT REGRESSION: PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host ("BOOT REGRESSION: FAIL - " + $failures.Count + " chequeo(s):") -ForegroundColor Red
    $failures | ForEach-Object { Write-Host ("  - " + $_) -ForegroundColor Red }
    exit 1
}
