param(
    [Parameter(Mandatory = $true)]
    [string] $Wcr2,

    [Parameter(Mandatory = $true)]
    [string] $Maple,

    [string] $Out = "C:\Wcr2CliTest\out",
    [string] $StringNode = "CashItemSearch.img",
    [string] $SkillId = "3001004",
    [string] $ItemId = "2000000",
    [string] $GearId = "1002140",
    [string] $MapId = "100000000",
    [string] $AnimPath = "0100100.img\stand"
)

$ErrorActionPreference = "Stop"

function Invoke-Wcr2 {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,

        [string] $StdoutPath,
        [switch] $AllowFailure
    )

    Write-Host "== $Name =="
    if ($StdoutPath) {
        & $Wcr2 @Arguments 2>&1 | Tee-Object $StdoutPath
    } else {
        & $Wcr2 @Arguments
    }

    $code = $LASTEXITCODE
    Write-Host "exit=$code"
    if ($code -ne 0 -and -not $AllowFailure) {
        throw "$Name failed with exit code $code"
    }
}

function Get-FirstExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [string[]] $Candidates
    )

    foreach ($candidate in $Candidates) {
        if (Test-Path $candidate) {
            Write-Host "$Name = $candidate"
            return $candidate
        }
    }

    throw "No usable $Name input found. Tried: $($Candidates -join ', ')"
}

function Get-MapShardPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $MapleRoot,

        [Parameter(Mandatory = $true)]
        [string] $Id
    )

    $data = Join-Path $MapleRoot "Data"
    $mapNumber = [int] $Id
    $group = [math]::Floor($mapNumber / 100000000)
    $shard = [math]::Floor($mapNumber / 100000)
    $groupName = "Map$group"
    $shardName = "{0}_{1:D3}.wz" -f $groupName, $shard

    return Get-FirstExistingPath "Map" @(
        (Join-Path $data "Map\Map\$groupName\$shardName"),
        (Join-Path $data "Map"),
        (Join-Path $MapleRoot "Map.wz")
    )
}

New-Item -ItemType Directory -Force $Out | Out-Null

$Data = Join-Path $Maple "Data"
$Base = Join-Path $Maple "Base.wz"
$String = Get-FirstExistingPath "String" @((Join-Path $Data "String"), (Join-Path $Maple "String.wz"))
$Skill = Get-FirstExistingPath "Skill" @((Join-Path $Data "Skill"), (Join-Path $Maple "Skill.wz"))
$Item = Get-FirstExistingPath "Item" @((Join-Path $Data "Item"), (Join-Path $Maple "Item.wz"))
$Character = Get-FirstExistingPath "Character" @((Join-Path $Data "Character"), (Join-Path $Maple "Character.wz"))
$Gear = Get-FirstExistingPath "Gear" @((Join-Path $Data "Character\Cap"), (Join-Path $Data "Character"), (Join-Path $Maple "Character.wz"))
$Map = Get-MapShardPath $Maple $MapId
$Mob = Get-FirstExistingPath "Mob animation" @((Join-Path $Data "Mob_Canvas"), (Join-Path $Data "Mob"), (Join-Path $Maple "Mob.wz"))

foreach ($path in @($Wcr2, $Base, $String, $Skill, $Item, $Character, $Gear, $Map, $Mob)) {
    if (-not (Test-Path $path)) {
        throw "Required path not found: $path"
    }
}

Invoke-Wcr2 "help" @("--help")
Invoke-Wcr2 "version" @("version")
Invoke-Wcr2 "info Base.wz" @("info", $Base, "--json") "$Out\info-base.json"
Invoke-Wcr2 "tree Base.wz" @("tree", $Base, "--depth", "2", "--limit", "200", "--json") "$Out\tree-base.json"
Invoke-Wcr2 "list Base.wz" @("list", $Base, "--json") "$Out\list-base.json"
Invoke-Wcr2 "search Base.wz" @("search", $Base, "--match-path", "*", "--max-results", "20", "--json") "$Out\search-base-path.json"
Invoke-Wcr2 "search String.wz" @("search", $String, "--name", "Skill", "--max-results", "20", "--json") "$Out\search-string-skill.json" -AllowFailure
Invoke-Wcr2 "tree String.wz" @("tree", $String, "--depth", "2", "--limit", "100", "--json") "$Out\tree-string.json"
Invoke-Wcr2 "dump String node" @("dump", $String, "--path", $StringNode, "--format", "json", "--out", "$Out\dump-string-node.json")
Invoke-Wcr2 "extract String node" @("extract", $String, "--path", $StringNode, "--out", "$Out\extract-string-node", "--recursive", "--manifest", "$Out\extract-string-node\manifest.json", "--json") "$Out\extract-string-node.json"
Invoke-Wcr2 "compare Base.wz with itself" @("compare", $Base, $Base, "--json", "--out", "$Out\compare-base-self.json")
Invoke-Wcr2 "skill info" @("skill", "info", $Skill, "--id", $SkillId, "--string-wz", $String, "--json") "$Out\skill-$SkillId.json"
Invoke-Wcr2 "item info" @("item", "info", $Item, "--id", $ItemId, "--string-wz", $String, "--json") "$Out\item-$ItemId.json"
Invoke-Wcr2 "gear info" @("gear", "info", $Gear, "--id", $GearId, "--string-wz", $String, "--json") "$Out\gear-$GearId.json"
Invoke-Wcr2 "map info" @("map", "info", $Map, "--id", $MapId, "--string-wz", $String, "--json") "$Out\map-$MapId.json" -AllowFailure
Invoke-Wcr2 "map portals" @("map", "portals", $Map, "--id", $MapId, "--json") "$Out\map-$MapId-portals.json" -AllowFailure
Invoke-Wcr2 "map life" @("map", "life", $Map, "--id", $MapId, "--json") "$Out\map-$MapId-life.json" -AllowFailure
Invoke-Wcr2 "map objects" @("map", "objects", $Map, "--id", $MapId, "--json") "$Out\map-$MapId-objects.json" -AllowFailure
Invoke-Wcr2 "map reactors" @("map", "reactors", $Map, "--id", $MapId, "--json") "$Out\map-$MapId-reactors.json" -AllowFailure
Invoke-Wcr2 "tree Mob.wz" @("tree", $Mob, "--depth", "3", "--limit", "200", "--json") "$Out\tree-mob.json"
Invoke-Wcr2 "animate frames" @("animate", "frames", $Mob, "--path", $AnimPath, "--out", "$Out\anim-frames", "--json") "$Out\anim-frames.json" -AllowFailure
Invoke-Wcr2 "config set" @("config", "set", "default-wz", $Base, "--config", "$Out\wcr2.config.json", "--json") "$Out\config-set.json"
Invoke-Wcr2 "config default input" @("info", "--config", "$Out\wcr2.config.json", "--json") "$Out\info-config-default.json"
Invoke-Wcr2 "avatar inspect" @("avatar", "inspect", "--code", "1002140,1040036,1060026", "--json") "$Out\avatar-inspect.json"
Invoke-Wcr2 "plugin list" @("plugin", "list", "--json") "$Out\plugin-list.json"

Write-Host "Smoke output: $Out"
