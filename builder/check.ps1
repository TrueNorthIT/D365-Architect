<#
.SYNOPSIS
Validate an exported schema folder offline: every file parses, every reference resolves.
No network, no environment. Run it after an export, and after an agent edits the files.

.EXAMPLE
./builder/check.ps1
#>
param([string]$Path = "$PSScriptRoot/../schema")

$ErrorActionPreference = "Stop"
Import-Module powershell-yaml -WarningAction SilentlyContinue
$Path = (Resolve-Path $Path).Path
$errors = [Collections.ArrayList]@()
function Fail($msg) { [void]$errors.Add($msg) }

$manifest = ConvertFrom-Yaml (Get-Content "$Path/solution.yaml" -Raw)
if (-not $manifest.tables) { Fail "solution.yaml lists no tables" }

foreach ($t in $manifest.tables) {
    $dir = "$Path/$t"
    if (-not (Test-Path "$dir/table.yaml")) { Fail "$t : table.yaml missing"; continue }

    $table = ConvertFrom-Yaml (Get-Content "$dir/table.yaml" -Raw)
    if ($table.logicalName -ne $t) { Fail "$t : table.yaml says logicalName '$($table.logicalName)'" }
    if (-not $table.primaryName) { Fail "$t : no primaryName" }

    $names = @($table.columns | ForEach-Object { $_.logicalName })
    foreach ($d in $names | Group-Object | Where-Object Count -gt 1) { Fail "$t : duplicate column '$($d.Name)'" }
    foreach ($c in $table.columns) {
        if (-not $c.type) { Fail "$t.$($c.logicalName) : no type" }
        if ($c.type -eq "Lookup" -and -not $c.targets) { Fail "$t.$($c.logicalName) : lookup with no targets" }
        if ($c.type -in "String", "Memo" -and -not $c.maxLength) { Fail "$t.$($c.logicalName) : $($c.type) with no maxLength" }
    }

    if (-not (Test-Path "$dir/views.yaml")) { continue }
    $views = ConvertFrom-Yaml (Get-Content "$dir/views.yaml" -Raw)
    foreach ($v in $views) {
        if (-not $v.fetch) { Fail "$t / $($v.name) : no fetch"; continue }
        # layout is genuinely absent on the auto-generated "My <table>" views.
        foreach ($k in "fetch", "layout") {
            if (-not $v.$k) { continue }
            $f = "$dir/$($v.$k)"
            if (-not (Test-Path $f)) { Fail "$t / $($v.name) : $k file missing ($($v.$k))"; continue }
            try { [void][xml](Get-Content $f -Raw) } catch { Fail "$t / $($v.name) : $k is not valid XML - $($_.Exception.Message)" }
        }
        if (-not $v.layout) { continue }
        # A column in the layout but not the fetch renders blank; the reverse is a hidden column,
        # which is legitimate. Only the first direction is a fault.
        $fetch = [xml](Get-Content "$dir/$($v.fetch)" -Raw)
        # The platform selects the primary name and id whether or not the fetch names them,
        # which is how the OOB "... I Follow" views get away with omitting them.
        $selected = @($table.primaryName, $table.primaryId) +
                    @($fetch.SelectNodes("//attribute") | ForEach-Object { $_.name }) +
                    @($fetch.SelectNodes("//link-entity") | ForEach-Object {
                          $alias = $_.alias; $_.SelectNodes("attribute") | ForEach-Object { "$alias.$($_.name)" } })
        foreach ($cell in ([xml](Get-Content "$dir/$($v.layout)" -Raw)).SelectNodes("//cell")) {
            if ($selected -notcontains $cell.name) { Fail "$t / $($v.name) : layout shows '$($cell.name)', fetch does not select it" }
        }
    }
}

$choices = "$Path/_global/choices.yaml"
if (Test-Path $choices) {
    $sets = ConvertFrom-Yaml (Get-Content $choices -Raw)
    foreach ($c in $sets) {
        foreach ($d in @($c.options | ForEach-Object { $_.value }) | Group-Object | Where-Object Count -gt 1) {
            Fail "choice $($c.name) : duplicate option value $($d.Name)"
        }
    }
}

$rels = "$Path/_global/relationships.yaml"
if (Test-Path $rels) {
    $defs = ConvertFrom-Yaml (Get-Content $rels -Raw)
    foreach ($r in $defs) {
        foreach ($end in @($r.referencedEntity, $r.referencingEntity, $r.entity1, $r.entity2) | Where-Object { $_ }) {
            if ($manifest.tables -notcontains $end) { Fail "relationship $($r.schemaName) : '$end' is not in the manifest" }
        }
    }
}

if ($errors) {
    $errors | ForEach-Object { Write-Host "  ! $_" -ForegroundColor Red }
    Write-Host "`n$($errors.Count) problem(s) in $Path" -ForegroundColor Red
    exit 1
}
Write-Host "OK - $($manifest.tables.Count) table(s) in $Path" -ForegroundColor Green
