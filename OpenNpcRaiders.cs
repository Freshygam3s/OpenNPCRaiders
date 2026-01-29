using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using Rust;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "1.3.4")]
    [Description("NPC Raiders with custom Roadsign/M249/AK gear sets and HP scaling")]
    public class OpenNpcRaiders : RustPlugin
    {
        private string _activePrefab;

        #region Configuration
        private ConfigData config;
        private class ConfigData
        {
            public RaidSettings Raid = new RaidSettings();
        }
        private class RaidSettings
        {
            public float DespawnTime = 600f;
            public bool ParachuteEntry = true;
            public float SpawnRadius = 45f;
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();
        protected override void LoadConfig() { base.LoadConfig(); config = Config.ReadObject<ConfigData>(); }
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        void Init() => ResolvePrefab();

        private void ResolvePrefab()
        {
            _activePrefab = StringPool.Get("assets/rust.ai/agents/npcplayerapex/scientist/scientistfull_heavy.prefab") != 0 
                ? "assets/rust.ai/agents/npcplayerapex/scientist/scientistfull_heavy.prefab" 
                : "scientistnpc_full_any";
        }

        [ChatCommand("npcrraid")]
        private void CmdNpcRaid(BasePlayer player, string command, string[] args)
        {
            if (player.net.connection.authLevel < 2) return;
            
            BuildingPrivlidge tc = GetRandomToolCupboard();
            if (tc == null) 
            {
                SendReply(player, "Error: No owned TC found on map.");
                return;
            }

            string diff = args.Length > 0 ? args[0].ToLower() : "normal";
            StartUltimateRaid(tc.transform.position, diff);
            
            SendReply(player, $"<color=#ff4444>RAID EVENT STARTED!</color> Difficulty: <color=yellow>{diff.ToUpper()}</color>");
        }

        private void StartUltimateRaid(Vector3 targetPos, string diff)
        {
            int count = 5; 
            bool spawnBoss = false;

            switch (diff)
            {
                case "easy":   
                    count = 3; 
                    spawnBoss = false; 
                    break;
                case "normal": 
                    count = 5; 
                    spawnBoss = false; 
                    break;
                case "hard":   
                    count = 8; 
                    spawnBoss = true;  
                    break;
                case "boss":   
                    count = 5; 
                    spawnBoss = true;  
                    break; 
                default:       
                    count = 5; 
                    spawnBoss = false; 
                    break;
            }

            // Spawn standard squad
            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos = GetSpawnPoint(targetPos);
                if (config.Raid.ParachuteEntry) spawnPos.y += 50f;
                SpawnRaider(spawnPos, _activePrefab, false, diff);
            }

            // Spawn Commander if applicable
            if (spawnBoss)
            {
                Vector3 bossSpawn = GetSpawnPoint(targetPos);
                if (config.Raid.ParachuteEntry) bossSpawn.y += 55f;
                SpawnRaider(bossSpawn, _activePrefab, true, diff);
            }

            CreateMapMarker(targetPos);
        }

        private void SpawnRaider(Vector3 pos, string prefab, bool isBoss, string diff)
        {
            BaseEntity entity = GameManager.server.CreateEntity(prefab, pos);
            if (entity == null) return;

            BasePlayer npc = entity as BasePlayer;
            npc.displayName = isBoss ? "ELITE COMMANDER" : "Raider";
            npc.Spawn();

            if (config.Raid.ParachuteEntry)
            {
                BaseEntity chute = GameManager.server.CreateEntity("assets/prefabs/misc/parachute/parachute.prefab", pos);
                if (chute != null)
                {
                    chute.SetParent(npc);
                    chute.Spawn();
                }
            }

            npc.inventory.Strip();
            
            if (isBoss)
            {
                if (diff == "hard")
                {
                    // HARD Commander: AK-47 + Roadsign Set + 300HP
                    npc.inventory.GiveItem(ItemManager.CreateByName("rifle.ak", 1), npc.inventory.containerBelt);
                    npc.inventory.GiveItem(ItemManager.CreateByName("roadsign.jacket", 1), npc.inventory.containerWear);
                    npc.inventory.GiveItem(ItemManager.CreateByName("roadsign.kilt", 1), npc.inventory.containerWear);
                    npc.inventory.GiveItem(ItemManager.CreateByName("coffeecan.helmet", 1), npc.inventory.containerWear);
                    npc.InitializeHealth(300f, 300f);
                }
                else if (diff == "boss")
                {
                    // BOSS Commander: M249 + 200HP
                    npc.inventory.GiveItem(ItemManager.CreateByName("lmg.m249", 1), npc.inventory.containerBelt);
                    npc.InitializeHealth(200f, 200f);
                }
            }
            else
            {
                // Standard Raiders
                string weapon = (diff == "hard" || diff == "boss") ? "rifle.ak" : "smg.mp5";
                npc.inventory.GiveItem(ItemManager.CreateByName(weapon, 1), npc.inventory.containerBelt);
            }

            npc.inventory.GiveItem(ItemManager.CreateByName("ammo.rifle", 256), npc.inventory.containerMain);
            npc.inventory.GiveItem(ItemManager.CreateByName("medical.syringe", 2), npc.inventory.containerMain);

            var activeWeapon = npc.inventory.containerBelt.GetSlot(0);
            if (activeWeapon != null) npc.UpdateActiveItem(activeWeapon.uid);

            timer.Once(config.Raid.DespawnTime, () => { if (npc != null && !npc.IsDestroyed) npc.Kill(); });
        }

        #region Helpers
        private Vector3 GetSpawnPoint(Vector3 target)
        {
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle.normalized * config.Raid.SpawnRadius;
            Vector3 pos = new Vector3(target.x + randomCircle.x, 0, target.z + randomCircle.y);
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 2f;
            return pos;
        }

        private void CreateMapMarker(Vector3 pos)
        {
            BaseEntity marker = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", pos);
            if (marker != null) marker.Spawn();
            timer.Once(config.Raid.DespawnTime, () => { if (marker != null) marker.Kill(); });
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
            {
                if (p != null && p.IsNpc && (p.displayName == "Raider" || p.displayName == "ELITE COMMANDER"))
                    p.Kill();
            }
        }
        #endregion
    }
}
