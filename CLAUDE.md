# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Scripts for editing Dynamics 365 / Dataverse customisations directly through the Web API, instead of clicking through make.powerapps.com. Anything doable in the maker portal is doable here: views (`savedqueries`), forms (`systemforms`), tables and columns (`EntityDefinitions`), relationships (`RelationshipDefinitions`), and row data.

## Layout

- `dv.ps1` — the transport. Everything is built on it.
- `builder/` — the declarative pipeline: export an environment to YAML, validate it, apply it back.
- `manual/` — the original one-change-at-a-time scripts. Still the right tool for a single view or choice.
- `schema/` — generated output of `builder/export.ps1`. Regenerate it, don't hand-fix it.

## builder/

**`export.ps1`** — dump a solution's tables, columns, choices, relationships and views to `schema/`.
Read-only.

```powershell
./builder/export.ps1                                       # Redcentric from DEV -> ./schema
./builder/export.ps1 -Solution Redcentric -Environment UAT -Out ./schema-uat
```

Output is one folder per table (`table.yaml`, `views.yaml`, `views/<name>.fetch.xml` + `.layout.xml`),
plus `_global/choices.yaml`, `_global/relationships.yaml` and a `solution.yaml` manifest. View XML
lives in sidecar files, not inline in YAML — it diffs and edits as XML, which is the whole point.

**`check.ps1`** — validate a `schema/` folder offline. No network, no environment. Run it after an
export and after anything edits the files.

```powershell
./builder/check.ps1
```

Catches the failure mode that matters most: a column in `layoutxml` that `fetchxml` doesn't select.

Scope is deliberate. Tables, columns, choices, relationships and views are modelled. Forms, charts
and dashboards are not yet. Plugins, flows, web resources and security roles never will be — those
belong to solution import, and this is not a replacement for it.

## Scripts

**`dv.ps1`** — thin wrapper over `/api/data/v9.2/`. Everything else is built on it.

```powershell
./dv.ps1 WhoAmI
./dv.ps1 "solutions?`$select=uniquename,friendlyname&`$filter=ismanaged eq false and isvisible eq true"
./dv.ps1 "savedqueries(<id>)" -Method PATCH -Body @{ fetchxml = "..." } -Solution Redcentric
```

**`views.ps1`** — list / read / diff / update system views for a table.

```powershell
./manual/views.ps1 reddt_raidlog                                  # list views
./manual/views.ps1 reddt_raidlog Assumptions                      # dump fetchxml + layoutxml
./manual/views.ps1 reddt_raidlog Assumptions -FetchXml $fx -LayoutXml $lx -WhatIf
```

**`choices.ps1`** — list / read / create / extend global choices (global option sets).

```powershell
./manual/choices.ps1                                              # list custom global choices
./manual/choices.ps1 reddt_responsetype                           # dump its options
./manual/choices.ps1 reddt_responsetype -DisplayName 'Response Type' -Options 'A','B' -WhatIf
```

`-Options` is the full desired list. Missing labels are inserted without a value, so Dataverse assigns one from the solution publisher's option prefix. Options present in the environment but absent from `-Options` are reported and left alone — deleting one orphans every row holding it, so do that by hand.

## Rules

- **Every script that can write must declare `[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]`** and gate the write behind `$PSCmdlet.ShouldProcess(...)`. `SupportsShouldProcess` on its own is **not** a safety net: it defaults to Medium impact, and with `$ConfirmPreference` at its default of High that means it never prompts — you get `-WhatIf` and a false sense of security. `dv.ps1` enforces this at the transport for any method other than GET, so a raw `-Method PATCH` asks too.
- `-Confirm:$false` is only for a caller that has *already* confirmed at a level where a human saw the diff — that is why `views.ps1` and `choices.ps1` pass it to `dv.ps1`. Never reach for it to make an unattended run go quiet.
- Non-interactive shells cannot answer the prompt, so a write there fails closed rather than proceeding. That is the intended behaviour; do not "fix" it with `-Confirm:$false`.
- **Always `-WhatIf` first**, show the diff, and get the developer's approval before writing. Writes publish immediately and are visible to everyone in the environment.
- Defaults are `-Environment DEV` and `-Solution Redcentric`. Never write to Test/UAT/Prod without being asked explicitly by name.
- Changing a column means editing **both** `fetchxml` and `layoutxml` — a column in only one of them silently does nothing. Prefer regex replaces that preserve the surrounding width/position attributes.
- Loop in the caller when updating several views; each view has its own XML. `$batch` exists but is not worth the multipart plumbing for a handful of PATCHes.
- Report diffs back to the user inside a ```diff fenced block — the `+`/`-` prefixes the script emits colour correctly in the terminal.

## Auth and environments

Token comes from the Azure CLI (`az account get-access-token`), so `az login` is the only prerequisite — no app registration, no client secret. Org URLs resolve from the `$Environments` table in `../alm/scripts/Common.ps1` by nickname (`DEV`, `Test`, `UAT`, `Prod`, ...), which stays the single source of truth.

Importing that file emits a harmless "unapproved verbs" warning from the PowerApps module on the warning stream. Data output is unaffected.

## Solutions

`-Solution` sets the `MSCRM.SolutionUniqueName` request header, the same mechanism the maker portal uses to decide where a change lands. A bogus name returns 404, so a successful write means the header was honoured.

A component may show no `solutioncomponent` row for the named solution. That is expected when its parent table is already a root component of that solution with *include subcomponents* — the change is still carried on export. Check with:

```powershell
./dv.ps1 "solutioncomponents?`$expand=solutionid(`$select=uniquename)&`$filter=objectid eq <id>"
```

## ALM

These edits land in the environment, not in source control. Getting them permanent still means exporting through the normal pipeline in `../power-platform` — see that folder's CLAUDE.md.
