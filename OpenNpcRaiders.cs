using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI; 
using Oxide.Core;
using Rust;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "1.7.2")]
    [Description("NPC Raiders: Fixed crouch logic using modelState and added stop command")]
    public class OpenNpcRaiders : RustPlugin
    {
        private const string ScientistRegular = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";
        private const string ScientistHeavy = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";

        #region Configuration
        private ConfigData config;
        private class ConfigData
        {
            public RaidSettings Raid = new RaidSettings();
        }
        private class RaidSettings
        {
            public float DespawnTime = 600f;
            public float SpawnRadius = 35f;
            public bool AttackStructures = true;
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();
        protected override void LoadConfig() { base.LoadConfig(); config = Config.ReadObject<ConfigData>(); }
        protected override void SaveConfig() => Config.WriteObject(config);
        #endregion

        [ChatCommand("npcrraid")]
        private void CmdNpcRaid(BasePlayer player, string cmd, string[] args)
        {
            if (player?.net?.connection == null || player.net.connection.authLevel < 2) return;

            BuildingPrivlidge tc = GetRandomToolCupboard();
            if (tc == null)
            {
                SendReply(player, "❌ No valid TCs found on map.");
                return;
            }

            string difficulty = args.Length > 0 ? args[0].ToLower() : "normal";
            SendReply(player, $"<color=orange>RAID INITIATED.</color> Squad heading to Grid {GetGrid(tc.transform.position)}");

            ExecuteStaggeredSpawn(tc.transform.position, difficulty);
        }

        [ChatCommand("npcrstop")]
        private void CmdNpcStop(BasePlayer player, string cmd, string[] args)
        {
            if (player?.net?.connection == null || player.net.connection.authLevel < 2) return;
            int count = KillAllRaiders();
            SendReply(player, $"<color=#ff4444>TERMINATED:</color> {count} active raiders removed.");
        }

        private void ExecuteStaggeredSpawn(Vector3 targetPos, string diff)
        {
            int count = (diff == "easy") ? 3 : (diff == "hard" ? 8 : 5);
            bool spawnBoss = (diff == "hard" || diff == "boss");

            for (int i = 0; i < count; i++)
                timer.Once(0.25f * i, () => SpawnRaider(targetPos, ScientistRegular, false, diff));

            if (spawnBoss)
                timer.Once(0.25f * (count + 1), () => SpawnRaider(targetPos, ScientistHeavy, true, diff));
        }

        private void SpawnRaider(Vector3 target, string prefab, bool isBoss, string diff)
        {
            Vector3 spawnPos = GetGroundPosition(target);
            BaseEntity entity = GameManager.server.CreateEntity(prefab, spawnPos);
            if (entity == null) return;

            ScientistNPC npc = entity as ScientistNPC;
            if (npc == null) { entity.Kill(); return; }

            npc.displayName = isBoss ? "ELITE COMMANDER" : "Raider";
            npc.Spawn();

            if (npc.Brain != null)
            {
                npc.Brain.SetEnabled(true);
                npc.Brain.ThinkMode = AIThinkMode.Interval;
            }

            npc.inventory.Strip();
            SetupLoadout(npc, isBoss, diff);

            timer.Once(2f, () => {
                if (npc == null || npc.IsDestroyed) return;
                
                // STAND UP: Using modelState instead of PlayerFlags
                npc.modelState.ducked = false;
                npc.SendNetworkUpdate();

                npc.SetDestination(target);
                InitializeRaidBehavior(npc, target);
            });

            timer.Once(config.Raid.DespawnTime, () => { if (npc != null && !npc.IsDestroyed) npc.Kill(); });
        }

        private void InitializeRaidBehavior(ScientistNPC npc, Vector3 target)
        {
            timer.Repeat(5f, 0, () =>
            {
                if (npc == null || npc.IsDestroyed || npc.IsDead()) return;

                // Don't interrupt if they are currently engaging a player
                if (npc.Brain.Senses.Memory.All.Count > 0) return;

                float distance = Vector3.Distance(npc.transform.position, target);

                if (distance > 8f)
                {
                    // Ensure they aren't ducking/crouching while moving
                    if (npc.modelState.ducked)
                    {
                        npc.modelState.ducked = false;
                        npc.SendNetworkUpdate();
                    }
                    npc.SetDestination(target);
                }
                else if (config.Raid.AttackStructures)
                {
                    List<BaseEntity> nearby = Pool.GetList<BaseEntity>();
                    Vis.Entities(npc.transform.position, 6f, nearby, Layers.Mask.Construction | Layers.Mask.Deployed);
                    foreach (var ent in nearby)
                    {
                        if (ent is BuildingBlock || ent is Door) 
                        { 
                            npc.Brain.Senses.Memory.SetKnown(ent, npc, null); 
                            break; 
                        }
                    }
                    Pool.FreeList(ref nearby);
                }
            });
        }

        private void SetupLoadout(ScientistNPC npc, bool isBoss, string diff)
        {
            if (isBoss)
            {
                if (diff == "hard")
                {
                    npc.inventory.GiveItem(ItemManager.CreateByName("rifle.ak", 1), npc.inventory.containerBelt);
                    npc.inventory.GiveItem(ItemManager.CreateByName("roadsign.jacket", 1), npc.inventory.containerWear);
                    npc.inventory.GiveItem(ItemManager.CreateByName("roadsign.kilt", 1), npc.inventory.containerWear);
                    npc.inventory.GiveItem(ItemManager.CreateByName("coffeecan.helmet", 1), npc.inventory.containerWear);
                    npc.InitializeHealth(300f, 300f);
                }
                else if (diff == "boss")
                {
                    npc.inventory.GiveItem(ItemManager.CreateByName("lmg.m249", 1), npc.inventory.containerBelt);
                    npc.InitializeHealth(200f, 200f);
                }
            }
            else
            {
                string weapon = (diff == "hard" || diff == "boss") ? "rifle.ak" : "smg.mp5";
                npc.inventory.GiveItem(ItemManager.CreateByName(weapon, 1), npc.inventory.containerBelt);
            }
            npc.inventory.GiveItem(ItemManager.CreateByName("ammo.rifle", 256), npc.inventory.containerMain);
            
            var weaponItem = npc.inventory.containerBelt.GetSlot(0);
            if (weaponItem != null) npc.UpdateActiveItem(weaponItem.uid);
        }

        #region Helpers
        private int KillAllRaiders()
        {
            int killed = 0;
            foreach (var p in BasePlayer.allPlayerList)
            {
                if (p != null && p.IsNpc && (p.displayName == "Raider" || p.displayName == "ELITE COMMANDER"))
                {
                    p.Kill();
                    killed++;
                }
            }
            return killed;
        }

        private Vector3 GetGroundPosition(Vector3 center)
        {
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * config.Raid.SpawnRadius;
            Vector3 pos = center + new Vector3(randomCircle.x, 0, randomCircle.y);
            pos.y = TerrainMeta.HeightMap.GetHeight(pos) + 1f;
            return pos;
        }

        private string GetGrid(Vector3 pos)
        {
            float offset = ConVar.Server.worldsize / 2;
            int col = Mathf.FloorToInt((pos.x + offset) / 146.3f);
            int row = Mathf.FloorToInt((offset - pos.z) / 146.3f);
            return $"{(char)('A' + col)}{row}";
        }

        private BuildingPrivlidge GetRandomToolCupboard()
        {
            var tcs = UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>();
            List<BuildingPrivlidge> valid = new List<BuildingPrivlidge>();
            foreach (var tc in tcs) if (tc != null && tc.OwnerID != 0) valid.Add(tc);
            return valid.Count == 0 ? null : valid.GetRandom();
        }

        void Unload() => KillAllRaiders();
        #endregion
    }
}
