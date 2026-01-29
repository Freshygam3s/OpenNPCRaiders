using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using Rust;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "1.2.5")]
    [Description("Fixed NPC Raiders using StringPool and confirmed shortnames")]
    public class OpenNpcRaiders : RustPlugin
    {
        private string _activePrefab;

        #region Configuration
        private ConfigData config;
        private class ConfigData
        {
            public DamageSettings Damage = new DamageSettings();
            public RaidSettings Raid = new RaidSettings();
        }
        private class DamageSettings
        {
            public bool DamagePlayers = true;
            public bool DamageBuildings = true;
        }
        private class RaidSettings
        {
            public int MinEasy = 2, MaxEasy = 3;
            public int MinNormal = 3, MaxNormal = 6;
            public int MinHard = 5, MaxHard = 8;
            public float SpawnRadius = 45f;
            public float DespawnTime = 420f;
            public bool AllowOfflineRaids = true; 
            public bool ShowMapMarker = true; 
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();
        protected override void LoadConfig() { base.LoadConfig(); config = Config.ReadObject<ConfigData>(); }
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        void Init() => ResolvePrefabPath();

        private void ResolvePrefabPath()
        {
            // List of potential full paths for different server versions
            string[] possibilities = new string[]
            {
                "assets/rust.ai/agents/npcplayerapex/scientist/scientistfull_heavy.prefab",
                "assets/prefabs/npc/scientist/scientistfull_heavy.prefab",
                "assets/rust.ai/agents/npcplayerapex/scientist/scientist_scavenger.prefab",
                "assets/prefabs/npc/scientist/scientist.prefab",
		"assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc.prefab",
		"assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab"
            };

            foreach (var path in possibilities)
            {
                // StringPool.Get is the gold standard for checking if a prefab exists in memory
                if (StringPool.Get(path) != 0)
                {
                    _activePrefab = path;
                    Puts($"[SUCCESS] Valid prefab found: {path}");
                    return;
                }
            }

            // If full paths fail, we use the shortname your console confirmed
            _activePrefab = "scientistnpc_full_any";
            Puts($"[WARNING] No full path found. Falling back to shortname: {_activePrefab}");
        }

        [ChatCommand("npcrraid")]
        private void CmdNpcRaid(BasePlayer player, string command, string[] args)
        {
            if (player.net.connection.authLevel < 2) { SendReply(player, "Admin access required."); return; }
            
            BuildingPrivlidge tc = GetRandomToolCupboard();
            if (tc == null) 
            { 
                SendReply(player, "Error: No owned Tool Cupboards found on map."); 
                return; 
            }

            string difficulty = args.Length > 0 ? args[0].ToLower() : "normal";
            StartRaid(tc.transform.position, difficulty);
            
            string grid = GetGrid(tc.transform.position);
            SendReply(player, $"<color=orange>RAID BEGUN!</color> Location: <color=#5af>Grid {grid}</color>");
        }

        private void StartRaid(Vector3 targetPos, string difficulty)
        {
            int min = config.Raid.MinNormal, max = config.Raid.MaxNormal;
            if (difficulty == "easy") { min = config.Raid.MinEasy; max = config.Raid.MaxEasy; }
            else if (difficulty == "hard") { min = config.Raid.MinHard; max = config.Raid.MaxHard; }

            int count = UnityEngine.Random.Range(min, max + 1);

            for (int i = 0; i < count; i++) 
                SpawnRaider(GetSpawnPoint(targetPos), _activePrefab);

            if (config.Raid.ShowMapMarker)
                CreateMapMarker(targetPos);
        }

        private void SpawnRaider(Vector3 spawnPos, string prefabPath)
        {
            BaseEntity entity = GameManager.server.CreateEntity(prefabPath, spawnPos);
            if (entity == null) 
            {
                Puts($"[ERROR] Failed to create entity with prefab: {prefabPath}");
                return;
            }

            BasePlayer npc = entity as BasePlayer;
            if (npc == null) 
            { 
                entity.Kill(); 
                return; 
            }

            npc.displayName = "Raider";
            npc.Spawn();

            // Loadout
            npc.inventory.Strip();
            npc.inventory.GiveItem(ItemManager.CreateByName("rifle.ak", 1), npc.inventory.containerBelt);
            npc.inventory.GiveItem(ItemManager.CreateByName("ammo.rifle", 256), npc.inventory.containerMain);
            
            var weapon = npc.inventory.containerBelt.GetSlot(0);
            if (weapon != null) npc.UpdateActiveItem(weapon.uid);

            npc.Teleport(spawnPos);

            // Despawn Timer
            timer.Once(config.Raid.DespawnTime, () => { if (npc != null && !npc.IsDestroyed) npc.Kill(); });
        }

        #region Helpers
        private Vector3 GetSpawnPoint(Vector3 target)
        {
            Vector3 pos = target + (UnityEngine.Random.insideUnitSphere.normalized * config.Raid.SpawnRadius);
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 2f;
            return pos;
        }

        private void CreateMapMarker(Vector3 pos)
        {
            BaseEntity marker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", pos);
            if (marker != null) marker.Spawn();
            timer.Once(config.Raid.DespawnTime, () => { if (marker != null) marker.Kill(); });
        }

        private string GetGrid(Vector3 pos)
        {
            float worldSize = ConVar.Server.worldsize;
            float offset = worldSize / 2;
            int col = Mathf.FloorToInt((pos.x + offset) / 146.3f);
            int row = Mathf.FloorToInt((offset - pos.z) / 146.3f);
            return $"{(char)('A' + col)}{row}";
        }

        private BuildingPrivlidge GetRandomToolCupboard()
        {
            var tcs = UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>();
            List<BuildingPrivlidge> valid = new List<BuildingPrivlidge>();
            foreach (var tc in tcs) if (tc != null && tc.OwnerID != 0) valid.Add(tc);
            return valid.Count == 0 ? null : valid[UnityEngine.Random.Range(0, valid.Count)];
        }

        void Unload()
        {
            foreach (var p in BasePlayer.allPlayerList)
                if (p != null && p.IsNpc && p.displayName == "Raider") p.Kill();

            foreach (var ent in BaseNetworkable.serverEntities)
                if (ent != null && ent.PrefabName.Contains("vending_mapmarker")) ent.Kill();
        }
        #endregion
    }
}
