#Requires -Version 5.1
<#
.SYNOPSIS
Thin wrapper over the Dataverse Web API. Auth via the Azure CLI token.

.EXAMPLE
./dv.ps1 WhoAmI
./dv.ps1 "savedqueries?`$select=name,fetchxml&`$filter=returnedtypecode eq 'account'"
./dv.ps1 "savedqueries(<id>)" -Method PATCH -Body @{ fetchxml = "<fetch>..." }
#>
param(
    [Parameter(Mandatory)][string]$Path,
    [ValidateSet("GET", "POST", "PATCH", "DELETE")][string]$Method = "GET",
    $Body,
    [string]$Environment = "DEV",
    [string]$Url,
    [string]$Solution                         # unique name; changed components land in this solution
)

$ErrorActionPreference = "Stop"

if (-not $Url) {
    # #requires in Common.ps1 auto-imports these noisily; pre-import quietly so it stays silent
    Import-Module Microsoft.PowerApps.Administration.PowerShell, Microsoft.Xrm.Data.Powershell -WarningAction SilentlyContinue
    . "$PSScriptRoot/../alm/scripts/Common.ps1"
    $Url = Get-DynamicsUrlByName (Get-DynamicsNameByNickname $Environment)
}
$Url = $Url.TrimEnd("/")

# Cached per org in user-scoped temp, else a bulk export pays ~1s per call to re-fetch it.
# Age, not the token's own expiry: az reports expiresOn in a locale-dependent format not worth parsing.
$cache = Join-Path ([IO.Path]::GetTempPath()) "dv-token-$($Url -replace '\W', '_').txt"
$token = if ((Test-Path $cache) -and (Get-Item $cache).LastWriteTime -gt (Get-Date).AddMinutes(-50)) {
    Get-Content $cache -Raw
}
if (-not $token) {
    $token = az account get-access-token --resource $Url --query accessToken -o tsv
    if (-not $token) { throw "No token for $Url - run 'az login'" }
    Set-Content $cache $token -NoNewline
}

$headers = @{
    Authorization      = "Bearer $token"
    "OData-Version"    = "4.0"
    "OData-MaxVersion" = "4.0"
    Accept             = "application/json"
    Prefer             = 'odata.include-annotations="*"'
}
if ($Solution) { $headers["MSCRM.SolutionUniqueName"] = $Solution }

$req = @{
    Uri         = "$Url/api/data/v9.2/$Path"
    Method      = $Method
    Headers     = $headers
    ContentType = "application/json; charset=utf-8"
}
if ($null -ne $Body) {
    $req.Body = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 30 -Compress }
    $req.Body = [Text.Encoding]::UTF8.GetBytes($req.Body)
    if ($Method -eq "PATCH") { $headers["If-Match"] = "*" }  # update only, never upsert-create
}

for ($attempt = 1; ; $attempt++) {
    try {
        Invoke-RestMethod @req
        break
    }
    catch {
        $r = $_.Exception.Response
        if ($r) {
            $detail = (New-Object IO.StreamReader($r.GetResponseStream())).ReadToEnd()
            # 429/503 are Dataverse throttling; everything else the server answered is a real error.
            if ($attempt -lt 4 -and [int]$r.StatusCode -in 429, 503) { Start-Sleep -Seconds ($attempt * 3); continue }
            throw "$($r.StatusCode) $Method $Path`n$detail"
        }
        # No response at all: DNS or socket. Bulk runs make hundreds of calls, some will blip.
        if ($attempt -lt 4) { Start-Sleep -Seconds ($attempt * 3); continue }
        throw
    }
}
