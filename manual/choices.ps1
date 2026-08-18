<#
.SYNOPSIS
List / read / create / extend global choices (global option sets). Always prints a diff before writing.

.EXAMPLE
./choices.ps1                                                    # list custom global choices
./choices.ps1 reddt_responsetype                                 # dump its options
./choices.ps1 reddt_responsetype -DisplayName 'Response Type' -Options 'A','B' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]   # High, else ShouldProcess never prompts
param(
    [string]$Name,                            # schema name, e.g. reddt_responsetype
    [string]$DisplayName,                     # defaults to $Name on create
    [string[]]$Options,                       # full desired list of labels
    [string]$Table,                           # optional: also add a column bound to this choice, e.g. hso_resourcerequest
    [switch]$Required,                        # column only, on create
    [string]$Default,                         # option label to default the column to, on create
    [string]$Environment = "DEV",
    [string]$Solution = "Redcentric"          # house default; override per call if a change belongs elsewhere
)

$ErrorActionPreference = "Stop"
$dv = { param($p, $m = "GET", $b) & "$PSScriptRoot/../dv.ps1" -Path $p -Method $m -Body $b -Environment $Environment -Solution $Solution -Confirm:$false }

function New-Label($text) {
    @{
        "@odata.type"    = "Microsoft.Dynamics.CRM.Label"
        LocalizedLabels  = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = $text; LanguageCode = 1033 })
    }
}

if (-not $Name) {
    (& $dv "GlobalOptionSetDefinitions?`$select=Name,IsCustomOptionSet").value |
        Where-Object IsCustomOptionSet | Select-Object Name, MetadataId | Sort-Object Name
    return
}

$existing = try { & $dv "GlobalOptionSetDefinitions(Name='$Name')" } catch { $null }

if (-not $Options) {
    if (-not $existing) { throw "No global choice named '$Name' in $Environment" }
    return $existing.Options | Select-Object @{n = "Label"; e = { $_.Label.UserLocalizedLabel.Label } }, Value
}

$have = @(if ($existing) { $existing.Options | ForEach-Object { $_.Label.UserLocalizedLabel.Label } })
$add = @($Options | Where-Object { $have -notcontains $_ })
$extra = @($have | Where-Object { $Options -notcontains $_ })
$column = if ($Table) { try { & $dv "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Name')?`$select=LogicalName" } catch { $null } }
$needColumn = [bool]($Table -and -not $column)

Write-Host "`n$Name  ($Environment, solution $Solution)" -ForegroundColor Cyan
if (-not $existing) { Write-Host "  + create global choice '$(if ($DisplayName) { $DisplayName } else { $Name })'" -ForegroundColor Green }
$add | ForEach-Object { Write-Host "  + $_" -ForegroundColor Green }
# Never auto-deleted: dropping an option value orphans every row that holds it.
$extra | ForEach-Object { Write-Host "  ! $_ (present in $Environment, not in -Options; left alone)" -ForegroundColor Yellow }
if ($needColumn) {
    $bits = @(if ($Required) { "mandatory" }; if ($Default) { "default '$Default'" })
    Write-Host "  + column $Name on $Table$(if ($bits) { " ($($bits -join ', '))" })" -ForegroundColor Green
}
elseif ($Table) { Write-Host "  = column $Name already on $Table (-Required/-Default apply on create only)" -ForegroundColor DarkGray }
if ($Default -and $Options -notcontains $Default) { throw "-Default '$Default' is not one of -Options" }
if (-not $add -and -not $needColumn -and $existing) { Write-Host "  (no change)" -ForegroundColor DarkGray }
Write-Host ""
if (-not $add -and -not $needColumn -and $existing) { return }

if (-not $PSCmdlet.ShouldProcess("$Name in $Environment", "create/extend global choice")) { return }

if (-not $existing) {
    & $dv "GlobalOptionSetDefinitions" POST @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        Name          = $Name
        DisplayName   = New-Label $(if ($DisplayName) { $DisplayName } else { $Name })
        OptionSetType = "Picklist"
        IsGlobal      = $true
        Options       = @()
    }
}

# No Value supplied: the platform assigns one from the solution publisher's option prefix, same as the maker portal.
foreach ($o in $add) {
    & $dv "InsertOptionValue" POST @{ OptionSetName = $Name; Label = (New-Label $o); SolutionUniqueName = $Solution }
}

if ($needColumn) {
    $set = & $dv "GlobalOptionSetDefinitions(Name='$Name')"
    $body = @{
        "@odata.type"              = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        SchemaName                 = $Name
        DisplayName                = New-Label $(if ($DisplayName) { $DisplayName } else { $Name })
        RequiredLevel              = @{ Value = $(if ($Required) { "ApplicationRequired" } else { "None" }) }
        AttributeTypeName          = @{ Value = "PicklistType" }
        "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($($set.MetadataId))"
    }
    # Option values are platform-assigned, so the default can only be resolved after the options exist.
    if ($Default) { $body.DefaultFormValue = ($set.Options | Where-Object { $_.Label.UserLocalizedLabel.Label -eq $Default }).Value }
    & $dv "EntityDefinitions(LogicalName='$Table')/Attributes" POST $body
}

$publish = if ($Table) { "<entities><entity>$Table</entity></entities>" } else { "<optionsets><optionset>$Name</optionset></optionsets>" }
& $dv "PublishXml" POST @{ ParameterXml = "<importexportxml>$publish</importexportxml>" }
"Updated and published '$Name' ($($add.Count) option(s) added$(if ($needColumn) { ", column on $Table" }))"
