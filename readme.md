Command line program to dump data about blueprints from a UE4 or UE5 game to Json files. This includes things like class and function metadata, class properties and disassembled function code.

## Installation

Releases can be found [here](https://github.com/CrystalFerrai/UeBlueprintDumper/releases).

This program is released standalone, meaning there is no installer. Simply extract the files to a directory to install it.

You will need to install the [.NET 8.0 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if you do not already have it.

## Usage

The first thing you will weant to do is either get the path to a specific asset, get the path to a folder containing assets you want to dump, or find a full or partial string that matches the names/paths of assets you want to dump. You can use a program like [FModel](https://fmodel.app/) to explore a game's data.

### Engine Version

You will need to locate the version of Unreal Engine that the game uses so you can pass it on the command line. Following is a list of all supported versions. If there is a game-specific version that matches the game you are working with, be sure to pass in that game specific version. Otherwise, pass in the version of the engine the game was built with.

```
UE4_0
UE4_1
UE4_2
UE4_3
UE4_4
UE4_5
ArkSurvivalEvolved
UE4_6
UE4_7
UE4_8
UE4_9
UE4_10
SeaOfThieves
UE4_11
GearsOfWar4
DaysGone
UE4_12
UE4_13
StateOfDecay2
WeHappyFew
UE4_14
TEKKEN7
TransformersOnline
UE4_15
ConanExiles
UE4_16
PlayerUnknownsBattlegrounds
TrainSimWorld2020
NarutotoBorutoShinobiStriker
UE4_17
AWayOut
UE4_18
KingdomHearts3
FinalFantasy7Remake
AceCombat7
FridayThe13th
GameForPeace
DragonQuestXI
UE4_19
Paragon
Ashen
UE4_20
Borderlands3
UE4_21
StarWarsJediFallenOrder
Undawn
UE4_22
UE4_23
ApexLegendsMobile
UE4_24
TonyHawkProSkater12
BigRumbleBoxingCreedChampions
UE4_25
UE4_25_Plus
RogueCompany
DeadIsland2
KenaBridgeofSpirits
Strinova
SYNCED
OperationApocalypse
Farlight84
StarWarsHunters
ThePathless
SuicideSquad
HellLetLoose
UE4_26
GTATheTrilogyDefinitiveEdition
ReadyOrNot
BladeAndSoul
TowerOfFantasy
FinalFantasy7Rebirth
TheDivisionResurgence
StarWarsJediSurvivor
Snowbreak
TorchlightInfinite
QQ
WutheringWaves
DreamStar
MidnightSuns
FragPunk
RacingMaster
StellarBlade
EtheriaRestart
EvilWest
ArenaBreakoutInfinite
Psychonauts2
UE4_27
Splitgate
HYENAS
HogwartsLegacy
OutlastTrials
Valorant_PRE_11_2
Gollum
Grounded
DeltaForceHawkOps
MortalKombat1
VisionsofMana
Spectre
KartRiderDrift
ThroneAndLiberty
MotoGP24
Stray
CrystalOfAtlan
PromiseMascotAgency
TerminullBrigade
AshEchoes
NeedForSpeedMobile
TonyHawkProSkater34
OnePieceAmbition
UnchartedWatersOrigin
LostSoulAside
GhostsofTabor
BlueProtocol
LittleNightmares3
Raven2
DuetNightAbyss
UE4_LATEST
UE5_0
MeetYourMaker
BlackMythWukong
UE5_1
3on3FreeStyleRebound
Stalker2
TheCastingofFrankStone
SilentHill2Remake
Dauntless
WorldofJadeDynasty
UE5_2
DeadByDaylight_Old
PaxDei
TheFirstDescendant
MetroAwakening
LostRecordsBloomAndRage
DuneAwakening
TitanQuest2
UE5_3
MarvelRivals
BlackStigma
Valorant
MonsterJamShowdown
Rennsport
AshesOfCreation
Avowed
MetalGearSolidDelta
UE5_4
FunkoFusion
InfinityNikki
NevernessToEverness_CBT1
Gothic1Remake
SplitFiction
WildAssault
InZOI
TempestRising
MindsEye
DeadByDaylight
Grounded2
MafiaTheOldCountry
2XKO
Reanimal
VEIN
GrayZoneWarfare
OuterWorlds2
UE5_5
Brickadia
Splitgate2
DeadzoneRogue
MotoGP25
Wildgate
ARKSurvivalAscended
NevernessToEverness
FateTrigger
Squad
Borderlands4
UE5_6
UE5_7
UE5_LATEST
```

To locate the engine version a game was built with, check the properties of the game's exe file. The version listed will be the Unreal Engine version.

### Encryption

If the pak files you are exporting from are encrypted, you will need to pass the optional parameter `--key [value]` where `[value]` is the AES key as a hexadecimal string.

### Mappings

For Unreal 5 games, you will also need to specify a mappings file using the optional parameter `--mappings [path]` where `[path]` is the path to a usmap file for the game. There are a few programs that can assist with generating a usmap file. The one that I personally use is [Dumper-7](https://github.com/Encryqed/Dumper-7) which is simple and effective for the games I have tested. You will need a DLL injector to inject Dumper-7. Any injector will work, but if you are looking for a simple one, you can use [DllInjector](https://github.com/CrystalFerrai/DllInjector).

Warning: If the game you are injecting into has anti-cheat software, the injection may be detected as a cheat.

### Example Usage

Dump all blueprints containing the string `MyGame/Content/BP` in their path from a game installed at `C:\Games\MyGame`. Write the output in the directory `C:\DumperOutput`.
```
UeBlueprintDumper "C:\Games\MyGame" UE4_27 "MyGame/Content/BP" "C:\DumperOutput"
```

### Command Line Parameter Reference

Run the program in a console window with no parameters to see a list of options like the following.

```
Usage: UeBlueprintDumper [[options]] [game directory] [asset match] [output directory]

  game directory    The directory of the game from which you want to dump blueprint data.

  engine version    The engine version the game was built with. See list below for values.

  asset match       A string matching one or more assets paths to dump. All assets with
                    paths containing this text will be processed.

  output directory  A file or directory that will receive the dumped blueprints.

Options

  --dump      Dump blueprints starting with the passed in path to the output diretory.
              This is the default behavior.

  --list      Output the names of all assets starting with the passed in path to a
              file named AssetList.txt in the output directory instead of dumping
              blueprint data. This includes assets which are not blueprints. Combine
              with --dump if you want to do both operations.

  --mappings  The path to a usmap file for the game. This is necessary if the game contains
              unversioned data, such as a UE5 game. See readme for more information.

  --key       The AES encryption key for the game's data if the data is encrypted.

Game engine versions
  Pass in the engine version that best matches the game being dumped. If the game has a
  specialized version, pass that in. Otherwise, pass in the engine version the game was
  built with, which can be found in the properties of the game's exe.

  (Will also print a full list of supported engine versions here.)
```

## Troubleshooting
If you are running into issues where blueprints cannot be found or are not being exported properly, it is usually due to one of the following issues.

1. The path to the game directory is incorrect. In most cases, this should be the directory where the game is installed.
2. You supplied the wrong engine version on the command line. Double check that it is correct.
3. The game's data is encrypted. You will need to obtain the decryption key and supply it on the command line.
4. The game requires a mappings file, and you have either not supplied one, supplied an incorrect one (such as from a different game), or supplied one for a different version of the game. All UE5 games require a mappings file, and in rare cases UE4 games also need them. Verify that you are supplying a valid one, if needed.
5. The version of CUE4Parse being used by this tool does not support the game you are attempting to export from. There is not much you can do about this aside from ask for an update or fork the repo and do your own update.

If you have verified everything above, but something is still not working, you can [open an issue](https://github.com/CrystalFerrai/UeBlueprintDumper/issues). Note that issues are not checked regularly, so there may be a long delay before you receive any response. 

## Building
Clone the repository, including submodules.
```
git clone --recursive https://github.com/CrystalFerrai/UeBlueprintDumper.git
```

You can then open and build UeBlueprintDumper.sln.

To publish a build for release, run this command from the directory containing the SLN.
```
dotnet publish -p:DebugType=None -r win-x64 -c Release --self-contained false
```

The resulting build can be located at `UeBlueprintDumper\bin\Release\net8.0\win-x64\publish`.