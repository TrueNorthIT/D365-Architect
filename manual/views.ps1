<#
.SYNOPSIS
List / read / update system views (savedqueries) for a table. Always prints a diff before writing.

.EXAMPLE
./views.ps1 account                          # list views
./views.ps1 account "Active Accounts"        # dump fetchxml + layoutxml
./views.ps1 account "Active Accounts" -FetchXml $fx -Solution Redcentric -WhatIf   # diff only, no write
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$Table,     # logical name, e.g. reddt_order
    [string]$View,                            # view name (exact)
    [string]$ViewId,                          # use instead of -View when two views share a name (matching is case-insensitive)
    [string]$CopyTo,                          # create a new view of this name from the source instead of updating it
    [string]$FetchXml,
    [string]$LayoutXml,
    [string]$Environment = "DEV",
    [string]$Solution = "Redcentric"          # house default; override per call if a change belongs elsewhere
)

$ErrorActionPreference = "Stop"
$dv = { param($p, $m = "GET", $b) & "$PSScriptRoot/../dv.ps1" -Path $p -Method $m -Body $b -Environment $Environment -Solution $Solution }

# Flatten a view into human-readable lines: what a developer actually sees in the view designer.
function Read-View($fetch, $layout) {
    $lines = [Collections.ArrayList]@()
    $widths = @{}
    if ($layout) {
        foreach ($c in ([xml]$layout).grid.row.cell) { $widths[$c.name] = $c.width }
    }
    if ($fetch) {
        $f = ([xml]$fetch).fetch.entity
        foreach ($a in $f.attribute) {
            $w = if ($widths.ContainsKey($a.name)) { " ($($widths[$a.name])px)" } else { " (hidden)" }
            [void]$lines.Add("column   $($a.name)$w")
        }
        foreach ($o in $f.order) {
            [void]$lines.Add("sort     $($o.attribute) $(if ($o.descending -eq 'true') { 'desc' } else { 'asc' })")
        }
        foreach ($c in $f.filter.condition) {
            [void]$lines.Add("filter   $($c.attribute) $($c.operator) $($c.value)")
        }
        # Linked columns are named alias.attribute in the layout, and nest arbitrarily deep.
        $walk = {
            param($node, $depth)
            foreach ($l in $node.'link-entity') {
                [void]$lines.Add("link     $('  ' * $depth)$($l.name) on $($l.from)=$($l.to) ($($l.'link-type')) as $($l.alias)")
                foreach ($a in $l.attribute) {
                    $n = "$($l.alias).$($a.name)"
                    $w = if ($widths.ContainsKey($n)) { " ($($widths[$n])px)" } else { " (hidden)" }
                    [void]$lines.Add("column   $n$w")
                }
                foreach ($c in $l.filter.condition) {
                    [void]$lines.Add("filter   $($l.alias).$($c.attribute) $($c.operator) $($c.value)")
                }
                & $walk $l ($depth + 1)
            }
        }
        & $walk $f 0
    }
    $lines
}

# Column order is diffed separately, else inserting one column renumbers everything and drowns the diff.
function Show-ViewDiff($before, $after, $beforeLayout, $afterLayout) {
    $changes = Compare-Object (Read-View @before) (Read-View @after)
    foreach ($c in $changes) {
        $sign = if ($c.SideIndicator -eq "=>") { "+" } else { "-" }
        Write-Host "  $sign $($c.InputObject)" -ForegroundColor $(if ($sign -eq "+") { "Green" } else { "Red" })
    }
    $cells = @($beforeLayout, $afterLayout) | ForEach-Object { , @(if ($_) { ([xml]$_).grid.row.cell.name } else { @() }) }
    # Ignore added/removed columns: a swap in place is already shown above, it is not also a reorder.
    $common = $cells[0] | Where-Object { $cells[1] -contains $_ }
    $ord = $cells | ForEach-Object { (($_ | Where-Object { $common -contains $_ }) -join ", ") }
    if ($ord[0] -ne $ord[1]) {
        Write-Host "  ~ order: $($ord[0])" -ForegroundColor Red
        Write-Host "  ~ order: $($ord[1])" -ForegroundColor Green
    }
    elseif (-not $changes) { Write-Host "  (no change)" -ForegroundColor DarkGray }
    [bool]($changes -or $ord[0] -ne $ord[1])
}

if (-not $View -and -not $ViewId) {
    (& $dv "savedqueries?`$select=name,querytype,isdefault,savedqueryid&`$filter=returnedtypecode eq '$Table'&`$orderby=name").value |
        Select-Object name, querytype, isdefault, savedqueryid
    return
}

if ($ViewId) {
    $q = & $dv "savedqueries($ViewId)?`$select=name,fetchxml,layoutxml,querytype,savedqueryid"
    $View = $q.name
}
else {
    $q = (& $dv "savedqueries?`$select=name,fetchxml,layoutxml,querytype,savedqueryid&`$filter=returnedtypecode eq '$Table' and name eq '$($View.Replace("'","''"))'").value
    if ($q.Count -ne 1) { throw "Expected 1 view named '$View' on $Table, found $($q.Count) - pass -ViewId instead" }
    $q = $q[0]
}

if (-not $FetchXml -and -not $LayoutXml -and -not $CopyTo) { return $q }

$new = @{ fetch = if ($FetchXml) { $FetchXml } else { $q.fetchxml }; layout = if ($LayoutXml) { $LayoutXml } else { $q.layoutxml } }

Write-Host "`n$Table / $(if ($CopyTo) { "$View -> $CopyTo" } else { $View })  ($Environment, solution $Solution)" -ForegroundColor Cyan
$changed = Show-ViewDiff @{ fetch = $q.fetchxml; layout = $q.layoutxml } $new $q.layoutxml $new.layout
Write-Host ""
if (-not $changed -and -not $CopyTo) { return }

$what = if ($CopyTo) { "create view '$CopyTo' from '$View'" } else { "update view" }
if (-not $PSCmdlet.ShouldProcess("$Table / $View in $Environment", $what)) { return }

if ($CopyTo) {
    # The source's savedqueryid is baked into its fetchxml; leaving it in points the copy at the original.
    & $dv "savedqueries" POST @{
        name             = $CopyTo
        returnedtypecode = $Table
        querytype        = $q.querytype
        fetchxml         = $new.fetch -replace ' savedqueryid="[^"]*"', ''
        layoutxml        = $new.layout
    }
}
else {
    $body = @{}
    if ($FetchXml) { $body.fetchxml = $FetchXml }
    if ($LayoutXml) { $body.layoutxml = $LayoutXml }
    & $dv "savedqueries($($q.savedqueryid))" PATCH $body
}

# Publish, else the change is invisible in the app
& $dv "PublishXml" POST @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" }
"$(if ($CopyTo) { "Created and published '$CopyTo'" } else { "Updated and published '$View'" }) on $Table"
