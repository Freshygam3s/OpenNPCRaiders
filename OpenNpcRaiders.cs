using System;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "0.3.0")]
    [Description("Open-source hybrid NPC raiders with daily random base raids")]
    public class OpenNpcRaiders : RustPlugin
    {
        #region Config

        private ConfigData config;

        private class ConfigData
        {
            public RaidSettings Raid = new RaidSettings();
            public DamageSettings Damage = new DamageSettings();
        }

        private class RaidSettings
        {
            public int MinNPCs = 3;
            public int MaxNPCs = 6;
            public float SpawnDistance = 50f;
            public float DespawnTime = 420f;

            public bool AllowOfflineRaids = true;
            public float OfflineRaidChance = 0.5f;

            public bool UseRockets = true;

            public float RaidIntervalSeconds = 86400f; // 1 day
        }

        private class DamageSettings
        {
            public bool DamagePlayers = true;
            public bool DamageBuildings = true;
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

        #region Hooks

        private void OnServerInitialized()
        {
            timer.Every(config.Raid.RaidIntervalSeconds, () =>
            {
                TryStartRandomRaid();
            });

            Puts($"Daily raid timer started ({config.Raid.RaidIntervalSeconds} seconds).");
        }

        #endregion

        #region Raid Logic

        private void TryStartRandomRaid()
        {
            var tc = GetRandomToolCupboard();
            if (tc == null)
            {
                Puts("No valid bases found for raid.");
                return;
            }

            BasePlayer owner = BasePlayer.FindByID(tc.OwnerID);

            if (owner == null || !owner.IsConnected)
            {
                if (!config.Raid.AllowOfflineRaids)
                    return;

                if (UnityEngine.Random.value > config.Raid.OfflineRaidChance)
                    return;
            }

            Vector3 targetPos = tc.transform.position;
            StartRaidAtPosition(targetPos);

            Puts($"NPC raid started on base owned by {tc.OwnerID}");
        }

        private void StartRaidAtPosition(Vector3 targetPos)
        {
            int count = UnityEngine.Random.Range(
                config.Raid.MinNPCs,
                config.Raid.MaxNPCs + 1
            );

            for (int i = 0; i < count; i++)
                SpawnRaider(GetSpawnPos(targetPos), targetPos);
        }

        private Vector3 GetSpawnPos(Vector3 target)
        {
            Vector3 pos = target + UnityEngine.Random.onUnitSphere * config.Raid.SpawnDistance;
            pos.y = TerrainMeta.HeightMap.GetHeight(pos);
            return pos;
        }

        private void SpawnRaider(Vector3 pos, Vector3 targetPos)
        {
            var npc = GameManager.server.CreateEntity(
                "assets/rust.ai/agents/npcplayerapex/npcplayerapex.prefab",
                pos
            ) as NPCPlayerApex;

            if (npc == null) return;

            npc.Spawn();
            npc.displayName = "Raider";

            npc.inventory.Strip();
            GiveLoadout(npc);

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

        private void GiveLoadout(NPCPlayerApex npc)
        {
            npc.inventory.GiveItem(
                ItemManager.CreateByName("rifle.ak", 1),
                npc.inventory.containerBelt
            );

            npc.inventory.GiveItem(
                ItemManager.CreateByName("ammo.rifle", 200),
                npc.inventory.containerMain
            );

            if (config.Raid.UseRockets)
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

        #region Admin Command (Manual)

        [ChatCommand("npcrraid")]
        private void CmdRaid(BasePlayer player, string cmd, string[] args)
        {
            if (!player.IsAdmin) return;

            TryStartRandomRaid();
            SendReply(player, "Random NPC raid triggered.");
        }

        #endregion
    }
}
