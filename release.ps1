# Publishes a Tinnitdown release:
#   1. asks for the version number (plain numbers, e.g. 0.1.0 -- never a leading "v")
#   2. writes it into Tinnitdown.csproj and app.manifest
#   3. builds the Native AOT exe
#   4. opens RELEASE_NOTES.md in VS Code and waits for you to save and close it
#   5. commits everything, tags the version, pushes, and creates the GitHub release with the exe
#   6. opens the release in your browser
#
# Run it with release.cmd (or: powershell -ExecutionPolicy Bypass -File release.ps1).
# Needs git, the .NET SDK, VS Code's "code" command and the GitHub CLI ("gh", signed in).
# Anything before the commit can be cancelled with Ctrl+C; the version files are put back.

#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$Repo = 'TonyProvolone/Tinnitdown'
$Project = 'Tinnitdown.csproj'
$Manifest = 'app.manifest'
$NotesFile = 'RELEASE_NOTES.md'
$ExeName = 'Tinnitdown.exe'
$FirstVersion = '0.1.0'

$NotesTemplate = @'
<!--
  Release notes for Tinnitdown {VERSION}. This becomes the GitHub release description.
  Write your notes, then SAVE and CLOSE this tab to continue the release.
  Sections left empty are removed, and so is this comment.
-->

# Features

-

# Fixes

-
'@

$script:Originals = @{}
$script:Committed = $false

function Fail([string]$message) { throw $message }

function Step([string]$message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Warn([string]$message) { Write-Host "    $message" -ForegroundColor Yellow }

# Runs a native command and stops the release if it fails.
function Invoke-Checked([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { Fail "$what failed (exit code $LASTEXITCODE)." }
}

# Runs a native command quietly and returns whether it succeeded. (Windows PowerShell turns redirected
# stderr into errors under 'Stop', so relax that while it runs.)
function Test-Native([scriptblock]$command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $command *> $null; return $LASTEXITCODE -eq 0 }
    finally { $ErrorActionPreference = $previous }
}

function Read-Text([string]$path) { [IO.File]::ReadAllText((Join-Path $PSScriptRoot $path)) }

# UTF-8 without BOM, LF line endings (matches .gitattributes).
function Write-Text([string]$path, [string]$text) {
    $text = $text -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot $path), $text, (New-Object Text.UTF8Encoding $false))
}

function Set-VersionedFile([string]$path, [string]$pattern, [string]$replacement) {
    $text = Read-Text $path
    if ($text -notmatch $pattern) { Fail "Couldn't find the version in $path." }
    if (-not $script:Originals.ContainsKey($path)) { $script:Originals[$path] = $text }
    Write-Text $path ([regex]::Replace($text, $pattern, $replacement))
}

function Restore-VersionFiles {
    foreach ($path in $script:Originals.Keys) { Write-Text $path $script:Originals[$path] }
    if ($script:Originals.Count -gt 0) { Write-Host "    Version files put back the way they were." -ForegroundColor DarkGray }
    $script:Originals = @{}
}

# Drops the instructions comment and any section that only has its heading or empty bullets.
function Get-CleanNotes([string]$text) {
    $text = [regex]::Replace($text, '(?s)<!--.*?-->', '')
    $blocks = @()
    $current = @()
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -match '^#{1,6}\s' -and $current.Count -gt 0) {
            $blocks += , $current
            $current = @()
        }
        $current += $line
    }
    $blocks += , $current

    $kept = foreach ($block in $blocks) {
        $content = @($block | Where-Object { $_ -notmatch '^\s*(#.*|[-*]\s*)?$' })
        if ($content.Count -gt 0) { ($block -join "`n").Trim() }
    }
    return (@($kept) -join "`n`n").Trim()
}

function Get-ReleaseVersions {
    $tags = @(git tag --list)
    if ($LASTEXITCODE -ne 0) { Fail 'Reading git tags failed.' }
    @($tags | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | ForEach-Object { [version]$_ } | Sort-Object)
}

function Read-ReleaseVersion {
    $latest = Get-ReleaseVersions | Select-Object -Last 1
    if ($latest) {
        $suggested = '{0}.{1}.{2}' -f $latest.Major, $latest.Minor, ($latest.Build + 1)
        Write-Host "    Latest release: $latest"
    }
    else {
        $suggested = $FirstVersion
        Write-Host '    No releases yet - this will be the first one.'
    }

    while ($true) {
        $answer = Read-Host "    Version number [$suggested]"
        if ([string]::IsNullOrWhiteSpace($answer)) { $answer = $suggested }
        $answer = $answer.Trim()

        if ($answer -match '^[vV]') { Warn "Leave off the 'v' - just numbers, like $suggested."; continue }
        if ($answer -notmatch '^\d+\.\d+\.\d+$') { Warn "Use three numbers (major.minor.patch), like $suggested."; continue }
        if ($latest -and [version]$answer -le $latest) { Warn "It has to be higher than the latest release ($latest)."; continue }
        if (git tag --list $answer) { Warn "Tag $answer already exists."; continue }
        if (git ls-remote --tags origin "refs/tags/$answer") { Warn "Tag $answer already exists on GitHub."; continue }
        return $answer
    }
}

function Edit-ReleaseNotes([string]$version) {
    $path = Join-Path $PSScriptRoot $NotesFile

    # Notes left over from a cancelled run (changed since the last commit) are reused, not overwritten.
    $isDraft = (Test-Path $path) -and ((git status --porcelain -- $NotesFile) -ne $null)
    if ($isDraft) {
        Write-Host "    Reusing the unfinished $NotesFile from last time."
    }
    else {
        Write-Text $NotesFile ($NotesTemplate -replace '\{VERSION\}', $version)
    }

    while ($true) {
        Write-Host "    Opened $NotesFile in VS Code. Write your notes, then save and close the tab to continue..." -ForegroundColor Green
        Invoke-Checked 'Opening VS Code' { code --wait $path }

        $notes = Get-CleanNotes (Read-Text $NotesFile)
        if ($notes) { return $notes }

        Warn 'The release notes are empty.'
        $choice = Read-Host '    [E]dit them again, or [A]bort the release? [E/a]'
        if ($choice -match '^[aA]') { Fail 'Cancelled.' }
    }
}

function Invoke-Release {
    Step 'Checking tools and repository'
    foreach ($tool in 'git', 'dotnet', 'code', 'gh') {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            if ($tool -eq 'gh') {
                Fail "The GitHub CLI isn't installed. Run 'winget install GitHub.cli', then 'gh auth login', then open a new terminal."
            }
            Fail "'$tool' isn't on your PATH."
        }
    }
    if (-not (Test-Native { gh auth status })) { Fail "The GitHub CLI isn't signed in. Run 'gh auth login' first." }

    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -eq 'HEAD') { Fail 'Check out a branch first (HEAD is detached).' }

    Invoke-Checked 'Fetching from GitHub' { git fetch origin --tags --quiet }
    if (Test-Native { git rev-parse --verify --quiet "origin/$branch" }) {
        $behind = [int](git rev-list --count "HEAD..origin/$branch")
        if ($behind -gt 0) { Fail "Your $branch branch is $behind commit(s) behind GitHub. Pull first." }
    }
    Write-Host "    Releasing from branch '$branch'."

    Step 'Version'
    $version = Read-ReleaseVersion

    Step "Setting version $version"
    Set-VersionedFile $Project '<Version>[^<]*</Version>' "<Version>$version</Version>"
    Set-VersionedFile $Manifest '(<assemblyIdentity\s+version=")[^"]*(")' "`${1}$version.0`${2}"
    Write-Host "    Updated $Project and $Manifest."

    Step 'Building'
    $outDir = Join-Path $PSScriptRoot "artifacts\release\$version"
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    Invoke-Checked 'The build' { dotnet publish $Project -c Release -o $outDir --nologo }

    $exe = Join-Path $outDir $ExeName
    if (-not (Test-Path $exe)) { Fail "The build didn't produce $ExeName." }
    $built = (Get-Item $exe).VersionInfo.FileVersion
    if ($built -ne "$version.0") { Fail "The exe reports version $built instead of $version.0." }
    Write-Host ("    Built {0} ({1:N1} MB)." -f $exe, ((Get-Item $exe).Length / 1MB))

    Step 'Release notes'
    $notes = Edit-ReleaseNotes $version
    Write-Text $NotesFile "$notes`n"

    Write-Host ''
    Write-Host '----- Release notes -----' -ForegroundColor DarkGray
    Write-Host $notes
    Write-Host '-------------------------' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host "    This commits ALL changes on '$branch', tags $version, pushes to GitHub and publishes the release."
    $confirm = Read-Host "    Publish Tinnitdown $version now? [y/N]"
    if ($confirm -notmatch '^[yY]') { Fail "Cancelled. Your notes are kept in $NotesFile for next time." }

    Step 'Committing and tagging'
    Invoke-Checked 'git add' { git add -A }
    Invoke-Checked 'git commit' { git commit -m "Release $version" }
    $script:Committed = $true
    $script:Originals = @{}
    Invoke-Checked 'git tag' { git tag -a $version -m "Tinnitdown $version" }

    Step 'Pushing to GitHub'
    $push = { git push --atomic origin $branch "refs/tags/$version" }
    & $push
    if ($LASTEXITCODE -ne 0) {
        Fail ("The push failed. The release is committed and tagged locally; once it's fixed, run:`n" +
            "      git push --atomic origin $branch refs/tags/$version`n" +
            "      gh release create $version `"$exe`" --repo $Repo --title `"Tinnitdown $version`" --notes-file $NotesFile --verify-tag")
    }

    Step 'Creating the GitHub release'
    & gh release create $version $exe --repo $Repo --title "Tinnitdown $version" --notes-file $NotesFile --verify-tag --latest
    if ($LASTEXITCODE -ne 0) {
        Fail ("Creating the release failed. The tag is already on GitHub; once it's fixed, run:`n" +
            "      gh release create $version `"$exe`" --repo $Repo --title `"Tinnitdown $version`" --notes-file $NotesFile --verify-tag")
    }

    $url = "https://github.com/$Repo/releases/tag/$version"
    Write-Host ''
    Write-Host "Released Tinnitdown $version" -ForegroundColor Green
    Write-Host "    $url"
    Start-Process $url
}

$failed = $false
try {
    Invoke-Release
}
catch {
    $failed = $true
    Write-Host ''
    Write-Host "Release stopped: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    # Ctrl+C or any failure before the commit: don't leave a half-bumped version behind.
    if (-not $script:Committed) { Restore-VersionFiles }
}
if ($failed) { exit 1 }
