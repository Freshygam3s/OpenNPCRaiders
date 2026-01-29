<div align="center">
🧨 OpenNpcRaiders
Dynamic NPC Raid Events for Rust (uMod / Oxide)

Version: 1.2.5
Author: FreshX

</div>
📖 Overview

OpenNpcRaiders is a Rust uMod/Oxide plugin that introduces dynamic NPC raid events using fully validated NPC prefabs. The plugin spawns armed NPC raiders near player-owned Tool Cupboards, creating immersive PvE raid scenarios that are both challenging and server-friendly.

To ensure long-term stability across Rust updates, the plugin automatically resolves valid NPC prefabs using StringPool, with a confirmed shortname fallback when required.

✨ Features

🧠 Smart Prefab Resolution
Automatically detects valid NPC prefabs at runtime using StringPool

⚔️ Dynamic NPC Raids
NPC raiders spawn near randomly selected player Tool Cupboards

🎚 Difficulty Levels
Supports Easy, Normal, and Hard raid tiers

🗺 Optional Map Marker
Displays a temporary marker at the raid location

⏱ Auto Despawn & Cleanup
NPCs and map markers are removed after a configurable duration

🔐 Admin Controlled
Raid events can only be initiated by server administrators

🕹 Commands
/npcrraid [easy | normal | hard]

Examples

/npcrraid → Starts a Normal raid

/npcrraid easy

/npcrraid hard

⚙ Configuration
{
  "Damage": {
    "DamagePlayers": true,
    "DamageBuildings": true
  },
  "Raid": {
    "MinEasy": 2,
    "MaxEasy": 3,
    "MinNormal": 3,
    "MaxNormal": 6,
    "MinHard": 5,
    "MaxHard": 8,
    "SpawnRadius": 45.0,
    "DespawnTime": 420.0,
    "AllowOfflineRaids": true,
    "ShowMapMarker": true
  }
}

Configuration Details
Setting	Description
SpawnRadius	Distance from the Tool Cupboard where NPCs spawn
DespawnTime	Time (seconds) before NPCs and markers are removed
ShowMapMarker	Enables or disables raid map markers
AllowOfflineRaids	Allows raids on offline player bases
🔫 NPC Loadout

AK Rifle

Rifle Ammo

Automatically equipped on spawn

NPCs are spawned as hostile raiders and cleaned up safely on plugin unload.

🧩 Compatibility

✔ Rust Dedicated Server

✔ uMod / Oxide

✔ Compatible across multiple Rust prefab versions

🔑 Permissions

Admin Only (authLevel ≥ 2)

🛠 Technical Notes

Uses StringPool.Get() for safe prefab validation

Falls back to confirmed shortnames if full paths are unavailable

Designed to minimize server impact and ensure stability

🧹 Cleanup Behavior

All active NPC raiders are removed on plugin unload

All temporary map markers are automatically destroyed

👤 Author

FreshX

<div align="center">

⭐ If you enjoy this plugin, consider sharing feedback or contributing! ⭐

</div>
