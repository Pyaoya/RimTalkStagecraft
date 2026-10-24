# Builds RimTalk - Stagecraft for RimWorld 1.6 (.NET Framework, against the game's Mono assemblies)
# and 1.7 (.NET 10, Build\Stagecraft17.csproj), then installs it into each game's Mods folder
# that is there. -NoInstall builds only.
param(
    [string]$Game16    = 'E:\SteamLibrary\steamapps\common\RimWorld',
    [string]$Game17    = 'E:\RimWorld-1.7-test',
    [string]$Harmony16 = 'E:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.6\Assemblies\0Harmony.dll',
    [string]$Harmony17 = 'E:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$Csc       = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    [switch]$NoInstall
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$sources = Get-ChildItem (Join-Path $here 'Source') -Filter *.cs | ForEach-Object { $_.FullName }

# The Chinese text, About.xml and the Workshop page text come from the player-edited Markdown file.
& py -3 (Join-Path $here 'Build\sync_text.py')
if ($LASTEXITCODE -ne 0) { throw "the Chinese text file has problems (see above); nothing was built" }

# 1.6
$managed = Join-Path $Game16 'RimWorldWin64_Data\Managed'
$out16 = Join-Path $here '1.6\Assemblies'
New-Item -ItemType Directory -Force $out16 | Out-Null
$refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll', 'Assembly-CSharp.dll', 'UnityEngine.CoreModule.dll',
          'UnityEngine.IMGUIModule.dll', 'UnityEngine.TextRenderingModule.dll' | ForEach-Object { '/r:' + (Join-Path $managed $_) })
$refs += "/r:$Harmony16"
& $Csc /nologo /target:library /nostdlib+ /noconfig /optimize+ /debug- /langversion:7.3 `
    "/out:$(Join-Path $out16 'RimTalkStagecraft.dll')" @refs @sources
if ($LASTEXITCODE -ne 0) { throw "1.6 build failed with exit code $LASTEXITCODE" }
"built 1.6"

# 1.7
& dotnet build (Join-Path $here 'Build\Stagecraft17.csproj') -c Release -nologo -v q "-p:Game17=$Game17" "-p:Harmony17=$Harmony17"
if ($LASTEXITCODE -ne 0) { throw "1.7 build failed with exit code $LASTEXITCODE" }
"built 1.7"

if ($NoInstall) { return }
# The game's Workshop uploader writes About\PublishedFileId.txt into the installed copy. Without it
# the next upload would make a second Workshop item, so it is brought back into the project first.
$ownId = Join-Path $here 'About\PublishedFileId.txt'
foreach ($game in $Game16, $Game17) {
    $theirs = Join-Path $game 'Mods\RimTalkStagecraft\About\PublishedFileId.txt'
    if ((Test-Path $theirs) -and -not (Test-Path $ownId)) { Copy-Item $theirs $ownId; "kept the Workshop id from $theirs" }
}
foreach ($game in $Game16, $Game17) {
    if (-not (Test-Path (Join-Path $game 'Mods'))) { continue }
    $dest = Join-Path $game 'Mods\RimTalkStagecraft'
    try {
        if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
        New-Item -ItemType Directory -Force $dest | Out-Null
        foreach ($d in 'About', 'Languages', '1.6', '1.7') { Copy-Item (Join-Path $here $d) (Join-Path $dest $d) -Recurse -Force }
        Copy-Item (Join-Path $here 'LoadFolders.xml') $dest
        "installed to $dest"
    }
    catch {
        # A running game may hold the old DLL open.
        Write-Warning "could not install to ${dest}: $($_.Exception.Message)"
    }
}
