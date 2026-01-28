using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "0.4.0")]
    [Description("Manual hybrid NPC raiders with difficulty levels")]
    public class OpenNpcRaiders : RustPlugin
    {
        #region Config

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
            public int MinNPCsEasy = 2;
            public int MaxNPCsEasy = 3;
            public int MinNPCsNormal = 3;
            public int MaxNPCsNormal = 6;
            public int MinNPCsHard = 5;
            public int MaxNPCsHard = 8;

            public float SpawnDistance = 50f;
            public float DespawnTime = 420f;

            public bool AllowOfflineRaids = true;
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

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion

        #region Admin Command

        [ChatCommand("npcrraid")]
        private void CmdRaid(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin)
            {
                SendReply(player, "You are not allowed to use this command.");
                return;
            }

            string difficulty = "normal";
            if (args.Length > 0)
                difficulty = args[0].ToLower();

            var tc = GetRandomToolCupboard();
            if (tc == null)
            {
                SendReply(player, "No valid bases found for a raid.");
                return;
            }

            BasePlayer owner = BasePlayer.FindByID(tc.OwnerID);

            if (owner == null || !owner.IsConnected)
            {
                if (!config.Raid.AllowOfflineRaids)
                {
                    SendReply(player, "Target is offline and offline raids are disabled.");
                    return;
                }

                if (UnityEngine.Random.value > config.Raid.OfflineRaidChance)
                {
                    SendReply(player, "Raid skipped due to offline chance.");
                    return;
                }
            }

            Vector3 targetPos = tc.transform.position;
            StartRaidAtPosition(targetPos, difficulty);
            SendReply(player, $"NPC raid started on base owned by {tc.OwnerID} with difficulty {difficulty}.");
        }

        #endregion

        #region Raid Logic

        private void StartRaidAtPosition(Vector3 targetPos, string difficulty)
        {
            int minNPC = config.Raid.MinNPCsNormal;
            int maxNPC = config.Raid.MaxNPCsNormal;
            bool rockets = config.Raid.NormalRockets;

            switch (difficulty)
            {
                case "easy":
                    minNPC = config.Raid.MinNPCsEasy;
                    maxNPC = config.Raid.MaxNPCsEasy;
                    rockets = config.Raid.EasyRockets;
                    break;
                case "hard":
                    minNPC = config.Raid.MinNPCsHard;
                    maxNPC = config.Raid.MaxNPCsHard;
                    rockets = config.Raid.HardRockets;
                    break;
            }

            int npcCount = UnityEngine.Random.Range(minNPC, maxNPC + 1);

            for (int i = 0; i < npcCount; i++)
                SpawnRaider(GetSpawnPos(targetPos), targetPos, rockets);
        }

        private Vector3 GetSpawnPos(Vector3 target)
        {
            Vector3 pos = target + UnityEngine.Random.onUnitSphere * config.Raid.SpawnDistance;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            return pos;
        }

        private void SpawnRaider(Vector3 pos, Vector3 targetPos, bool rockets)
        {
            var npc = GameManager.server.CreateEntity(
                "assets/rust.ai/agents/npcplayerapex/npcplayerapex.prefab",
                pos
            ) as NPCPlayerApex;

            if (npc == null) return;

            npc.Spawn();
            npc.displayName = "Raider";

            npc.inventory.Strip();
            GiveLoadout(npc, rockets);

            npc.SetFact(BaseNPC.Facts.IsAggro, 1);
            npc.SetDestination(targetPos);

            timer.Once(config.Raid.DespawnTime, () =>
            {
                if (npc != null && !npc.IsDestroyed)
                    npc.Kill();
            });
        }

        #endregion

        #region Tool Cupboard Logic

        private BuildingPrivlidge GetRandomToolCupboard()
        {
            var tcs = new List<BuildingPrivlidge>();

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is BuildingPrivlidge tc && tc.OwnerID != 0)
                    tcs.Add(tc);
            }

            if (tcs.Count == 0)
                return null;

            return tcs[UnityEngine.Random.Range(0, tcs.Count)];
        }

        #endregion

        #region Loadout

        private void GiveLoadout(NPCPlayerApex npc, bool rockets)
        {
            npc.inventory.GiveItem(
                ItemManager.CreateByName("rifle.ak", 1),
                npc.inventory.containerBelt
            );

            npc.inventory.GiveItem(
                ItemManager.CreateByName("ammo.rifle", 200),
                npc.inventory.containerMain
            );

            if (rockets)
            {
                npc.inventory.GiveItem(
                    ItemManager.CreateByName("rocket.launcher", 1),
                    npc.inventory.containerBelt
                );

                npc.inventory.GiveItem(
                    ItemManager.CreateByName("ammo.rocket.basic", 3),
                    npc.inventory.containerMain
                );
            }
        }

        #endregion

        #region Damage Control

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.Initiator is NPCPlayerApex)
            {
                if (entity is BasePlayer && !config.Damage.DamagePlayers)
                    return true;

                if (entity is BuildingBlock && !config.Damage.DamageBuildings)
                    return true;
            }
            return null;
        }

        #endregion
    }
}