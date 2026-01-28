OpenNpcRaiders Plugin for Rust

Description

OpenNpcRaiders is a manual hybrid NPC Raiders plugin for Rust. NPCs can attack both players and buildings, and raids are triggered manually by admins. The plugin supports difficulty levels that adjust the number of NPCs and their weapons.

Features

Manual admin-triggered raids (/npcrraid)

Random base selection using Tool Cupboards

Hybrid damage: players and buildings

Difficulty levels: easy, normal, hard

Configurable spawn distance and despawn timer

Fully open-source and modifiable


Installation

1. Place the plugin in the Rust server plugin folder:



/oxide/plugins/OpenNpcRaiders.cs

2. Reload the plugin in the server console or RCON:



oxide.reload OpenNpcRaiders

Commands

/npcrraid - Trigger a raid manually on a random base

/npcrraid easy - Easy difficulty raid

/npcrraid normal - Normal difficulty raid (default)

/npcrraid hard - Hard difficulty raid


Config Options

The plugin will create a config file at:

/oxide/config/OpenNpcRaiders.json

Raid Settings

Option	Description	Default

MinNPCsEasy	Minimum NPCs for easy difficulty	2
MaxNPCsEasy	Maximum NPCs for easy difficulty	3
MinNPCsNormal	Minimum NPCs for normal difficulty	3
MaxNPCsNormal	Maximum NPCs for normal difficulty	6
MinNPCsHard	Minimum NPCs for hard difficulty	5
MaxNPCsHard	Maximum NPCs for hard difficulty	8
SpawnDistance	Distance NPCs spawn from target	50
DespawnTime	Time in seconds before NPCs despawn	420
AllowOfflineRaids	Whether NPCs can raid offline bases	true
OfflineRaidChance	Chance an offline raid occurs	0.5
EasyRockets	Whether NPCs use rockets on easy	false
NormalRockets	Whether NPCs use rockets on normal	true
HardRockets	Whether NPCs use rockets on hard	true


Damage Settings

Option	Description	Default

DamagePlayers	NPCs can damage players	true
DamageBuildings	NPCs can damage buildings	true


How it Works

Admin triggers a raid using /npcrraid [difficulty]

Plugin selects a random base by Tool Cupboard ownership

NPCs spawn at random positions around the base

NPCs move toward the base and attack players/buildings

NPCs despawn after the configured timer


Notes

Only admins can trigger raids

Plugin does not run automatic or timed raids

Compatible with uMod/Oxide servers


License

MIT License – free to use, modify, and distribute.