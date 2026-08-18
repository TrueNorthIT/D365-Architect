<#
.SYNOPSIS
Dump a solution's tier-1 customisations (tables, columns, choices, relationships, views) to a
folder of YAML. Read-only. The output is the input to apply.ps1.

.EXAMPLE
./builder/export.ps1                                        # Redcentric from DEV -> ./schema
./builder/export.ps1 -Solution Redcentric -Environment UAT -Out ./schema-uat
#>
param(
    [string]$Solution = "Redcentric",
    [string]$Environment = "DEV",
    [string]$Out = "$PSScriptRoot/../schema"
)

$ErrorActionPreference = "Stop"
Import-Module powershell-yaml -WarningAction SilentlyContinue
$dv = { param($p) & "$PSScriptRoot/../dv.ps1" -Path $p -Environment $Environment }

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$utf8 = New-Object Text.UTF8Encoding($false)   # no BOM; Set-Content -Encoding utf8 emits one on PS 5.1

function Label($l) { if ($l -and $l.UserLocalizedLabel) { $l.UserLocalizedLabel.Label } }

function Write-Text($text, $path) {
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [IO.File]::WriteAllText($path, $text, $utf8)
}

function Write-Yaml($obj, $path) {
    Write-Text (ConvertTo-Yaml $obj) $path
    Write-Host "  $($path.Substring($Out.Length + 1))" -ForegroundColor DarkGray
}

# Dataverse hands back fetchxml/layoutxml either minified or pre-indented depending on which
# designer last touched it. Normalising both makes re-exports diff line-by-line instead of
# showing one 3000-char line as wholly changed.
function Format-Xml($xml) {
    if (-not $xml) { return $xml }
    $sw = New-Object IO.StringWriter
    $w = New-Object Xml.XmlTextWriter($sw)
    $w.Formatting = "Indented"
    $w.Indentation = 2
    ([xml]$xml).WriteContentTo($w)
    $w.Flush()
    $sw.ToString()
}

# --- what is in the solution -------------------------------------------------
$sid = ((& $dv "solutions?`$select=solutionid&`$filter=uniquename eq '$Solution'").value).solutionid
if (-not $sid) { throw "No solution '$Solution' in $Environment" }

$components = (& $dv "solutioncomponents?`$select=objectid,componenttype&`$filter=_solutionid_value eq $sid").value
$entityIds = @($components | Where-Object componenttype -eq 1 | ForEach-Object objectid)
$choiceIds = @($components | Where-Object componenttype -eq 9 | ForEach-Object objectid)
if (-not $entityIds) { throw "Solution '$Solution' has no tables" }

Write-Host "`n$Solution @ $Environment -> $Out" -ForegroundColor Cyan
Write-Host "$($entityIds.Count) table(s), $($choiceIds.Count) global choice(s)`n"

# --- columns -----------------------------------------------------------------
# Attributes are fetched without $select so each comes back as its full derived type
# (MaxLength, Targets, Precision...). OptionSet is the exception: it only arrives via
# an explicit expand on the cast collection, hence the extra pass below.
$optionSetCasts = @(
    "PicklistAttributeMetadata"
    "MultiSelectPicklistAttributeMetadata"
    "BooleanAttributeMetadata"
)

function Get-Columns($logicalName) {
    $all = (& $dv "EntityDefinitions(LogicalName='$logicalName')/Attributes").value |
        Where-Object { $_.IsCustomAttribute -and -not $_.AttributeOf -and -not $_.IsLogical }

    $sets = @{}
    foreach ($cast in $optionSetCasts) {
        $q = "EntityDefinitions(LogicalName='$logicalName')/Attributes/Microsoft.Dynamics.CRM.$cast" +
        "?`$select=LogicalName&`$expand=OptionSet,GlobalOptionSet"
        foreach ($a in (& $dv $q).value) { $sets[$a.LogicalName] = $a }
    }

    foreach ($a in $all | Sort-Object LogicalName) {
        $c = [ordered]@{
            logicalName = $a.LogicalName
            schemaName  = $a.SchemaName
            displayName = Label $a.DisplayName
            type        = $a.AttributeType
            required    = $a.RequiredLevel.Value
        }
        if (Label $a.Description) { $c.description = Label $a.Description }

        switch ($a.AttributeType) {
            { $_ -in "String", "Memo" } {
                $c.maxLength = $a.MaxLength
                if ($a.Format) { $c.format = $a.Format }
            }
            { $_ -in "Integer", "BigInt" } { $c.min = $a.MinValue; $c.max = $a.MaxValue }
            { $_ -in "Decimal", "Double", "Money" } {
                $c.precision = $a.Precision; $c.min = $a.MinValue; $c.max = $a.MaxValue
            }
            "DateTime" {
                if ($a.Format) { $c.format = $a.Format }
                if ($a.DateTimeBehavior) { $c.behavior = $a.DateTimeBehavior.Value }
            }
            "Lookup" { $c.targets = @($a.Targets) }
            default { }
        }

        $s = $sets[$a.LogicalName]
        if ($s) {
            if ($s.GlobalOptionSet) { $c.globalChoice = $s.GlobalOptionSet.Name }
            elseif ($s.OptionSet) {
                $c.options = @($s.OptionSet.Options | ForEach-Object {
                        [ordered]@{ label = Label $_.Label; value = $_.Value }
                    })
            }
            if ($null -ne $a.DefaultFormValue -and $a.DefaultFormValue -ne -1) { $c.default = $a.DefaultFormValue }
        }
        $c
    }
}

# --- views -------------------------------------------------------------------
# views.yaml is an index; the XML lives in sidecar files so it diffs and edits like XML.
function Get-Views($logicalName, $dir) {
    # ponytail: unmanaged only. Drops the hundreds of OOB views on system tables like systemuser,
    # but also drops an OOB view someone customised in place - those stay ismanaged=true.
    $q = "savedqueries?`$select=savedqueryid,name,querytype,isdefault,fetchxml,layoutxml,description" +
    "&`$filter=returnedtypecode eq '$logicalName' and ismanaged eq false&`$orderby=name"
    $views = (& $dv $q).value

    # View names are not unique in Dataverse, so the file stem is disambiguated and `id` is the
    # real key. Warn rather than fail: duplicates are the environment's problem, not the export's.
    foreach ($d in $views | Group-Object name | Where-Object Count -gt 1) {
        Write-Host "  ! $($d.Count) views named '$($d.Name)' - apply must match these on id" -ForegroundColor Yellow
    }

    $stems = @{}
    foreach ($v in $views) {
        $stem = $v.name -replace '[\\/:*?"<>|]', '_'
        if ($stems.ContainsKey($stem)) { $stems[$stem]++; $stem = "$stem-$($stems[$stem])" } else { $stems[$stem] = 1 }

        $o = [ordered]@{
            id        = $v.savedqueryid
            name      = $v.name
            queryType = $v.querytype
            isDefault = [bool]$v.isdefault
        }
        if ($v.description) { $o.description = $v.description }

        # savedqueryid is baked into the fetchxml and is environment-specific; the platform
        # re-stamps it on save, so dropping it keeps the file portable and the diffs stable.
        if ($v.fetchxml) {
            Write-Text (Format-Xml ($v.fetchxml -replace ' savedqueryid="[^"]*"', '')) "$dir/views/$stem.fetch.xml"
            $o.fetch = "views/$stem.fetch.xml"
        }
        # The auto-generated "My <table>" views carry no layoutxml at all - no file, no key.
        if ($v.layoutxml) {
            Write-Text (Format-Xml $v.layoutxml) "$dir/views/$stem.layout.xml"
            $o.layout = "views/$stem.layout.xml"
        }
        $o
    }
}

# --- tables ------------------------------------------------------------------
$tables = @()
$failed = [ordered]@{}
foreach ($id in $entityIds) {
    # One bad table must not sink a 100-table run; collect and report at the end.
    $e = $null
    try {
    $e = & $dv ("EntityDefinitions($id)?`$select=LogicalName,SchemaName,DisplayName," +
        "DisplayCollectionName,Description,OwnershipType,PrimaryNameAttribute,PrimaryIdAttribute")
    Write-Host $e.LogicalName -ForegroundColor White
    $tables += $e.LogicalName

    Write-Yaml ([ordered]@{
            logicalName    = $e.LogicalName
            schemaName     = $e.SchemaName
            displayName    = Label $e.DisplayName
            collectionName = Label $e.DisplayCollectionName
            description    = Label $e.Description
            ownership      = $e.OwnershipType
            primaryId      = $e.PrimaryIdAttribute
            primaryName    = $e.PrimaryNameAttribute
            columns        = @(Get-Columns $e.LogicalName)
        }) "$Out/$($e.LogicalName)/table.yaml"

    $views = @(Get-Views $e.LogicalName "$Out/$($e.LogicalName)")
    if ($views) { Write-Yaml $views "$Out/$($e.LogicalName)/views.yaml" }
    }
    catch {
        $name = if ($e) { $e.LogicalName } else { $id }
        $tables = @($tables | Where-Object { $_ -ne $name })
        $failed[$name] = ($_.Exception.Message -split "`n")[0]
        Write-Host "  ! $($failed[$name])" -ForegroundColor Red
    }
}

# --- global choices ----------------------------------------------------------
if ($choiceIds) {
    Write-Host "_global" -ForegroundColor White
    $choices = foreach ($id in $choiceIds) {
        $s = & $dv "GlobalOptionSetDefinitions($id)"
        [ordered]@{
            name        = $s.Name
            displayName = Label $s.DisplayName
            options     = @($s.Options | ForEach-Object { [ordered]@{ label = Label $_.Label; value = $_.Value } })
        }
    }
    Write-Yaml @($choices | Sort-Object { $_.name }) "$Out/_global/choices.yaml"
}

# --- relationships -----------------------------------------------------------
# Both ends must be in scope; a relationship to a table we did not export is not ours to recreate.
$inScope = { param($a, $b) $tables -contains $a -and $tables -contains $b }

$o2m = (& $dv ("RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata" +
        "?`$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencingAttribute,IsCustomRelationship,CascadeConfiguration")).value |
    Where-Object { $_.IsCustomRelationship -and (& $inScope $_.ReferencedEntity $_.ReferencingEntity) } |
    ForEach-Object {
        [ordered]@{
            type                 = "OneToMany"
            schemaName           = $_.SchemaName
            referencedEntity     = $_.ReferencedEntity
            referencingEntity    = $_.ReferencingEntity
            referencingAttribute = $_.ReferencingAttribute
            cascade              = [ordered]@{
                assign = $_.CascadeConfiguration.Assign
                delete = $_.CascadeConfiguration.Delete
                share  = $_.CascadeConfiguration.Share
            }
        }
    }

$m2m = (& $dv ("RelationshipDefinitions/Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata" +
        "?`$select=SchemaName,Entity1LogicalName,Entity2LogicalName,IntersectEntityName,IsCustomRelationship")).value |
    Where-Object { $_.IsCustomRelationship -and (& $inScope $_.Entity1LogicalName $_.Entity2LogicalName) } |
    ForEach-Object {
        [ordered]@{
            type       = "ManyToMany"
            schemaName = $_.SchemaName
            entity1    = $_.Entity1LogicalName
            entity2    = $_.Entity2LogicalName
            intersect  = $_.IntersectEntityName
        }
    }

$rels = @($o2m) + @($m2m) | Sort-Object { $_.schemaName }
if ($rels) { Write-Yaml $rels "$Out/_global/relationships.yaml" }

# --- manifest ----------------------------------------------------------------
Write-Yaml ([ordered]@{
        solution    = $Solution
        environment = $Environment
        exportedAt  = (Get-Date -Format "o")
        tables      = @($tables | Sort-Object)
    }) "$Out/solution.yaml"

Write-Host "`nDone. $($tables.Count) table(s) -> $Out" -ForegroundColor Green
if ($failed.Count) {
    Write-Host "$($failed.Count) table(s) failed and are NOT in the manifest:" -ForegroundColor Red
    $failed.GetEnumerator() | ForEach-Object { Write-Host "  $($_.Key): $($_.Value)" -ForegroundColor Red }
}
