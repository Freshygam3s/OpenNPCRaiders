using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using Rust;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "1.1.0")]
    [Description("NPC raiders with fixed command and simplified AI")]
    public class OpenNpcRaiders : RustPlugin
    {
        private const string AdminPerm = "opennpcraiders.admin";

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
            public bool AllowOfflineRaids = false;
            public float OfflineRaidChance = 0.5f;
            public bool EasyRockets = false, NormalRockets = true, HardRockets = true;
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();
        protected override void LoadConfig() { base.LoadConfig(); config = Config.ReadObject<ConfigData>(); }
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        void Init()
        {
            permission.RegisterPermission(AdminPerm, this);
        }

        #region Command
        [ChatCommand("npcrraid")]
        private void CmdNpcRaid(BasePlayer player, string command, string[] args)
        {
            // Fallback: If player is auth level 2 (Admin) or has the oxide permission
            if (player.net.connection.authLevel < 2 && !permission.UserHasPermission(player.UserIDString, AdminPerm))
            {
                SendReply(player, "<color=red>Permission Denied.</color> (Requires Admin or permission: opennpcraiders.admin)");
                return;
            }

            string difficulty = (args.Length > 0) ? args[0].ToLower() : "normal";
            BuildingPrivlidge tc = GetRandomToolCupboard();
            
            if (tc == null)
            {
                SendReply(player, "Error: No Tool Cupboards found on the map with an owner.");
                return;
            }

            if (!IsOwnerOnline(tc.OwnerID) && !CheckOfflineRaid())
            {
                SendReply(player, "Raid Cancelled: Owner is offline.");
                return;
            }

            StartRaid(tc.transform.position, difficulty);
            SendReply(player, $"<color=#5af>RAID INITIATED!</color> Level: {difficulty}.");
            Puts($"NPC Raid started at {tc.transform.position} for owner {tc.OwnerID}");
        }

        private bool IsOwnerOnline(ulong ownerID)
        {
            return BasePlayer.activePlayerList.Exists(p => p.userID == ownerID);
        }

        private bool CheckOfflineRaid() => config.Raid.AllowOfflineRaids && UnityEngine.Random.value <= config.Raid.OfflineRaidChance;
        #endregion

        #region Raid Logic
        private void StartRaid(Vector3 targetPos, string difficulty)
        {
            int min = config.Raid.MinNormal, max = config.Raid.MaxNormal;
            bool rockets = config.Raid.NormalRockets;

            if (difficulty == "easy") { min = config.Raid.MinEasy; max = config.Raid.MaxEasy; rockets = config.Raid.EasyRockets; }
            else if (difficulty == "hard") { min = config.Raid.MinHard; max = config.Raid.MaxHard; rockets = config.Raid.HardRockets; }

            int count = UnityEngine.Random.Range(min, max + 1);
            for (int i = 0; i < count; i++) 
            {
                SpawnRaider(GetSpawnPoint(targetPos), rockets, targetPos);
            }
        }

        private Vector3 GetSpawnPoint(Vector3 target)
        {
            Vector3 pos = target + (UnityEngine.Random.insideUnitSphere.normalized * config.Raid.SpawnRadius);
            float y = TerrainMeta.HeightMap.GetHeight(pos);
            pos.y = y + 1.5f;
            return pos;
        }

        private void SpawnRaider(Vector3 spawnPos, bool rockets, Vector3 tcPos)
        {
            // Using scientists prefabs as they have the best built-in AI for raiding/combat
            BaseEntity entity = GameManager.server.CreateEntity("assets/rust.ai/agents/npcplayerapex/scientist/scientistfull_heavy.prefab", spawnPos);
            if (entity == null) return;

            BasePlayer npc = entity as BasePlayer;
            if (npc == null) { entity.Kill(); return; }

            npc.displayName = "Raider";
            npc.Spawn();

            // Clear their default scientist loot and give raid gear
            SetupInventory(npc, rockets);
            
            // This is the most compatible way to move NPCs in Oxide
            // We teleport them slightly to 'wake' the NavMesh
            npc.Teleport(spawnPos);

            // Simple Despawn Timer
            timer.Once(config.Raid.DespawnTime, () => { if (npc != null && !npc.IsDestroyed) npc.Kill(); });
        }
        #endregion

        #region Inventory
        private void SetupInventory(BasePlayer npc, bool rockets)
        {
            npc.inventory.Strip();
            npc.inventory.GiveItem(ItemManager.CreateByName("rifle.ak", 1), npc.inventory.containerBelt);
            npc.inventory.GiveItem(ItemManager.CreateByName("ammo.rifle", 256), npc.inventory.containerMain);

            if (rockets)
            {
                npc.inventory.GiveItem(ItemManager.CreateByName("rocket.launcher", 1), npc.inventory.containerBelt);
                npc.inventory.GiveItem(ItemManager.CreateByName("ammo.rocket.basic", 8), npc.inventory.containerMain);
            }
            
            var weapon = npc.inventory.containerBelt.GetSlot(0);
            if (weapon != null) npc.UpdateActiveItem(weapon.uid);
        }
        #endregion

        #region Tool Cupboard Utility
        private BuildingPrivlidge GetRandomToolCupboard()
        {
            var allTcs = UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>();
            List<BuildingPrivlidge> validTcs = new List<BuildingPrivlidge>();

            foreach (var tc in allTcs)
                if (tc != null && tc.OwnerID != 0 && !tc.IsDestroyed) validTcs.Add(tc);

            return validTcs.Count == 0 ? null : validTcs[UnityEngine.Random.Range(0, validTcs.Count)];
        }
        #endregion

        #region Damage Control
        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.Initiator is BasePlayer attacker && attacker.IsNpc && attacker.displayName == "Raider")
            {
                if (entity is BasePlayer && !config.Damage.DamagePlayers) return true;
                if (entity is BuildingBlock && !config.Damage.DamageBuildings) return true;
            }
            return null;
        }
        #endregion

        void Unload()
        {
            foreach (var player in BasePlayer.allPlayerList)
                if (player != null && player.IsNpc && player.displayName == "Raider") player.Kill();
        }
    }
}
