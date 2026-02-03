using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using Rust;
using Facepunch; // Added for memory list handling

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "1.6.0")]
    [Description("NPC Raiders that spawn on ground and attack structures with fixed SetKnown logic")]
    public class OpenNpcRaiders : RustPlugin
    {
        private const string PlanePrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
        private string activeScientistPrefab;

        #region Configuration
        private ConfigData config;
        private class ConfigData
        {
            public RaidSettings Raid = new RaidSettings();
            public LootSettings Loot = new LootSettings();
            public PrefabSettings Prefabs = new PrefabSettings();
        }
        private class RaidSettings
        {
            public float DespawnTime = 600f;
            public float SpawnRadius = 35f;
            public bool ShowPlaneVisual = true;
            public float PlaneHeight = 200f;
            public bool AttackStructures = true;
            public float StructureAttackRange = 5f;
        }
        private class PrefabSettings
        {
            public List<string> ScientistPrefabs = new List<string>
            {
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab",
                "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab"
            };
        }
        private class LootSettings
        {
            public List<string> EasyLoot = new List<string> { "ammo.rifle", "scrap" };
            public List<string> NormalLoot = new List<string> { "ammo.rifle", "scrap", "techparts" };
            public List<string> HardLoot = new List<string> { "ammo.rifle", "hq.metal", "explosives" };
            public List<string> BossLoot = new List<string> { "lmg.m249", "explosives", "hq.metal" };
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<ConfigData>();
        }
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        void OnServerInitialized() => ResolveScientistPrefab();

        private void ResolveScientistPrefab()
        {
            foreach (var prefab in config.Prefabs.ScientistPrefabs)
            {
                if (GameManager.server.FindPrefab(prefab) != null)
                {
                    activeScientistPrefab = prefab;
                    return;
                }
            }
            activeScientistPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";
        }

        [ChatCommand("npcrraid")]
        private void CmdNpcRaid(BasePlayer player, string cmd, string[] args)
        {
            if (player?.net?.connection == null || player.net.connection.authLevel < 2) return;

            BuildingPrivlidge tc = GetRandomToolCupboard();
            if (tc == null)
            {
                SendReply(player, "❌ No valid TCs found.");
                return;
            }

            string difficulty = args.Length > 0 ? args[0].ToLower() : "normal";
            StartRaid(tc.transform.position, difficulty);
            SendReply(player, $"🔥 Raiders deployed! Target TC at {tc.transform.position}.");
        }

        private void StartRaid(Vector3 targetPos, string diff)
        {
            if (config.Raid.ShowPlaneVisual) SpawnVisualPlane(targetPos);

            int count = diff == "easy" ? 3 : diff == "hard" ? 8 : 5;
            bool spawnBoss = (diff == "hard" || diff == "boss");

            for (int i = 0; i < count; i++) SpawnRaider(targetPos, false, diff);
            if (spawnBoss) SpawnRaider(targetPos, true, diff);
        }

        private void SpawnVisualPlane(Vector3 target)
        {
            Vector3 start = target + new Vector3(-800f, config.Raid.PlaneHeight, 0);
            Vector3 end = target + new Vector3(800f, config.Raid.PlaneHeight, 0);
            BaseEntity plane = GameManager.server.CreateEntity(PlanePrefab, start);
            if (plane == null) return;
            plane.Spawn();
            plane.GetComponent<CargoPlane>()?.InitDropPosition(end);
            timer.Once(60f, () => { if (plane != null && !plane.IsDestroyed) plane.Kill(); });
        }

        private void SpawnRaider(Vector3 target, bool isBoss, string diff)
        {
            Vector3 spawnPos = GetGroundPosition(target);
            BaseEntity entity = GameManager.server.CreateEntity(activeScientistPrefab, spawnPos);
            if (entity == null) return;

            entity.Spawn();
            ScientistNPC npc = entity as ScientistNPC;
            if (npc == null) { entity.Kill(); return; }

            timer.Once(2f, () => {
                if (npc == null || npc.IsDestroyed) return;
                npc.SetDestination(target);
                if (config.Raid.AttackStructures) InitializeRaidBehavior(npc, target);
            });

            npc.displayName = isBoss ? "ELITE COMMANDER" : "Raider";
            npc.inventory.Strip();
            GiveGear(npc, isBoss, diff);
            GiveLoot(npc, isBoss, diff);

            timer.Once(config.Raid.DespawnTime, () => { if (npc != null && !npc.IsDestroyed) npc.Kill(); });
        }

        private void InitializeRaidBehavior(ScientistNPC npc, Vector3 target)
        {
            timer.Repeat(5f, 0, () =>
            {
                if (npc == null || npc.IsDestroyed || npc.IsDead()) return;

                if (Vector3.Distance(npc.transform.position, target) < 20f)
                {
                    // If NPC already knows about something (like a player), don't get distracted by walls
                    if (npc.Brain?.Senses?.Memory?.All != null && npc.Brain.Senses.Memory.All.Count > 0)
                        return;

                    List<BaseEntity> nearby = Pool.GetList<BaseEntity>();
                    Vis.Entities(npc.transform.position, config.Raid.StructureAttackRange, nearby, Layers.Mask.Construction | Layers.Mask.Deployed);

                    foreach (var ent in nearby)
                    {
                        if (ent is Door || ent is BuildingBlock)
                        {
                            // FIXED: Use the 2-argument overload (Entity, ThreatSource) 
                            // This is the most compatible version across Rust updates
                            npc.Brain.Senses.Memory.SetKnown(ent, npc, null);
                            npc.SetDestination(npc.transform.position); 
                            break; 
                        }
                    }
                    Pool.FreeList(ref nearby);
                }
            });
        }

        private void GiveGear(BasePlayer npc, bool isBoss, string diff)
        {
            string weapon = isBoss ? (diff == "boss" ? "lmg.m249" : "rifle.ak") : (diff == "hard" ? "rifle.ak" : "smg.mp5");
            npc.inventory.GiveItem(ItemManager.CreateByName(weapon, 1), npc.inventory.containerBelt);
            if (isBoss) npc.InitializeHealth(400f, 400f);
        }

        private void GiveLoot(BasePlayer npc, bool isBoss, string diff)
        {
            List<string> table = isBoss ? config.Loot.BossLoot : (diff == "easy" ? config.Loot.EasyLoot : (diff == "hard" ? config.Loot.HardLoot : config.Loot.NormalLoot));
            for (int i = 0; i < (isBoss ? 4 : 2); i++)
            {
                var itemDef = ItemManager.FindItemDefinition(table.GetRandom());
                if (itemDef != null) npc.inventory.containerMain.AddItem(itemDef, UnityEngine.Random.Range(1, 5));
            }
        }

        private Vector3 GetGroundPosition(Vector3 center)
        {
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * config.Raid.SpawnRadius;
            Vector3 randomPos = center + new Vector3(randomCircle.x, 0, randomCircle.y);
            randomPos.y = TerrainMeta.HeightMap.GetHeight(randomPos) + 0.5f;
            return randomPos;
        }

        private BuildingPrivlidge GetRandomToolCupboard()
        {
            var allTcs = UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>();
            List<BuildingPrivlidge> validTcs = new List<BuildingPrivlidge>();
            foreach (var tc in allTcs)
            {
                if (tc != null && tc.OwnerID != 0 && !tc.IsDestroyed) 
                    validTcs.Add(tc);
            }
            return validTcs.Count == 0 ? null : validTcs.GetRandom();
        }
    }
}
