using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using Rust;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "1.0.5")]
    [Description("Manual NPC raiders with native AI targeting")]
    public class OpenNpcRaiders : RustPlugin
    {
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
            public int MinEasy = 2;
            public int MaxEasy = 3;
            public int MinNormal = 3;
            public int MaxNormal = 6;
            public int MinHard = 5;
            public int MaxHard = 8;

            public float SpawnRadius = 45f;
            public float DespawnTime = 420f;

            public bool AllowOfflineRaids = false;
            public float OfflineRaidChance = 0.5f;

            public bool EasyRockets = false;
            public bool NormalRockets = true;
            public bool HardRockets = true;
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<ConfigData>();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion

        #region Command

        [ChatCommand("npcrraid")]
        private void CmdNpcRaid(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You are not allowed to use this command.");
                return;
            }

            string difficulty = args.Length > 0 ? args[0].ToLower() : "normal";

            BuildingPrivlidge tc = GetRandomToolCupboard();
            if (tc == null)
            {
                SendReply(player, "No valid tool cupboards found.");
                return;
            }

            if (!IsOwnerOnline(tc.OwnerID) && !CheckOfflineRaid())
            {
                SendReply(player, "Offline raid was blocked.");
                return;
            }

            StartRaid(tc.transform.position, difficulty);
            SendReply(player, $"NPC raid started ({difficulty}) on TC owned by {tc.OwnerID}");
        }

        private bool IsOwnerOnline(ulong ownerID)
        {
            foreach (BasePlayer p in BasePlayer.activePlayerList)
            {
                if (p.userID == ownerID) return true;
            }
            return false;
        }

        private bool CheckOfflineRaid()
        {
            return config.Raid.AllowOfflineRaids &&
                   UnityEngine.Random.value <= config.Raid.OfflineRaidChance;
        }

        #endregion

        #region Raid Logic

        private void StartRaid(Vector3 targetPos, string difficulty)
        {
            int min, max;
            bool rockets;

            switch (difficulty)
            {
                case "easy":
                    min = config.Raid.MinEasy;
                    max = config.Raid.MaxEasy;
                    rockets = config.Raid.EasyRockets;
                    break;
                case "hard":
                    min = config.Raid.MinHard;
                    max = config.Raid.MaxHard;
                    rockets = config.Raid.HardRockets;
                    break;
                default:
                    min = config.Raid.MinNormal;
                    max = config.Raid.MaxNormal;
                    rockets = config.Raid.NormalRockets;
                    break;
            }

            int count = UnityEngine.Random.Range(min, max + 1);

            for (int i = 0; i < count; i++)
                SpawnRaider(GetSpawnPoint(targetPos), rockets, targetPos);
        }

        private Vector3 GetSpawnPoint(Vector3 target)
        {
            Vector3 pos = target + UnityEngine.Random.insideUnitSphere * config.Raid.SpawnRadius;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 1f;
            return pos;
        }

        private void SpawnRaider(Vector3 spawnPos, bool rockets, Vector3 tcPos)
        {
            BaseEntity entity = GameManager.server.CreateEntity(
                "assets/rust.ai/agents/npcplayerapex/npcplayerapex.prefab",
                spawnPos
            );

            if (entity == null) return;

            // Cast as NPCPlayerApex to access specialized AI methods
            NPCPlayerApex npc = entity as NPCPlayerApex;
            if (npc == null)
            {
                entity.Kill();
                return;
            }

            npc.displayName = "Raider";
            npc.InitializeHealth(250f, 250f);
            npc.Spawn();

            SetupInventory(npc, rockets);
            
            // Native AI: Set destination to the TC position
            npc.SetHomePos(tcPos);
            
            timer.Once(config.Raid.DespawnTime, () =>
            {
                if (npc != null && !npc.IsDestroyed)
                    npc.Kill();
            });
        }

        #endregion

        #region Inventory

        private void SetupInventory(BasePlayer npc, bool rockets)
        {
            npc.inventory.Strip();

            npc.inventory.GiveItem(
                ItemManager.CreateByName("rifle.ak", 1),
                npc.inventory.containerBelt
            );

            npc.inventory.GiveItem(
                ItemManager.CreateByName("ammo.rifle", 256),
                npc.inventory.containerMain
            );

            if (rockets)
            {
                npc.inventory.GiveItem(
                    ItemManager.CreateByName("rocket.launcher", 1),
                    npc.inventory.containerBelt
                );

                npc.inventory.GiveItem(
                    ItemManager.CreateByName("ammo.rocket.basic", 6),
                    npc.inventory.containerMain
                );
            }

            if (npc.inventory.containerBelt.itemList.Count > 0)
                npc.UpdateActiveItem(npc.inventory.containerBelt.itemList[0].uid);
        }

        #endregion

        #region Tool Cupboard

        private BuildingPrivlidge GetRandomToolCupboard()
        {
            List<BuildingPrivlidge> tcs = new List<BuildingPrivlidge>();

            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                if (entity is BuildingPrivlidge tc && tc.OwnerID != 0)
                    tcs.Add(tc);
            }

            if (tcs.Count == 0) return null;

            return tcs[UnityEngine.Random.Range(0, tcs.Count)];
        }

        #endregion

        #region Damage Control

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            BasePlayer attacker = info?.Initiator as BasePlayer;
            if (attacker != null && attacker.IsNpc && attacker.displayName == "Raider")
            {
                if (entity is BasePlayer && !config.Damage.DamagePlayers)
                    return true;

                if (entity is BuildingBlock && !config.Damage.DamageBuildings)
                    return true;
            }

            return null;
        }

        #endregion

        #region Cleanup

        void Unload()
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BasePlayer npc && npc.IsNpc && npc.displayName == "Raider")
                {
                    npc.Kill();
                }
            }
        }

        #endregion
    }
}
