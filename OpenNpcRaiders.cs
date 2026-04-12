using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Oxide.Core;
using Oxide.Core.Plugins;
using Facepunch;
using Rust.Ai;

namespace Oxide.Plugins
{
    [Info("OpenNpcRaiders", "FreshX", "2.0.0")]
    [Description("Spawns NPC raiders to attack a random player base. Use /npcrraid [easy|normal|hard|boss]")]
    public class OpenNpcRaiders : RustPlugin
    {
        // ═══════════════════════════════════════════════════════════
        //  CONSTANTS
        // ═══════════════════════════════════════════════════════════
        private const string PREFAB_REGULAR = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_full_any.prefab";
        private const string PREFAB_HEAVY   = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab";

        private const string PERM_USE  = "opennpcraiders.use";
        private const string PERM_STOP = "opennpcraiders.stop";

        // ═══════════════════════════════════════════════════════════
        //  RAID TRACKING
        // ═══════════════════════════════════════════════════════════
        // All actively tracked raider NPCs — used for cleanup and /npcrstop
        private readonly HashSet<ScientistNPC> _activeRaiders = new HashSet<ScientistNPC>();

        // ═══════════════════════════════════════════════════════════
        //  CONFIGURATION
        // ═══════════════════════════════════════════════════════════
        private ConfigData _config;

        private class ConfigData
        {
            public RaidSettings Raid { get; set; } = new RaidSettings();
        }

        private class RaidSettings
        {
            public float DespawnTime      { get; set; } = 600f;
            public float SpawnRadius      { get; set; } = 35f;
            public float AttackRadius     { get; set; } = 8f;
            public bool  AttackStructures { get; set; } = true;
            public int   EasyCount        { get; set; } = 3;
            public int   NormalCount      { get; set; } = 5;
            public int   HardCount        { get; set; } = 8;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();
            Puts("Default configuration created.");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigData>();
                if (_config?.Raid == null) throw new Exception("Null config section.");
                ValidateConfig();
            }
            catch (Exception ex)
            {
                PrintWarning($"Config error ({ex.Message}), reverting to defaults.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private void ValidateConfig()
        {
            bool dirty = false;

            if (_config.Raid.DespawnTime  < 30f) { _config.Raid.DespawnTime  = 30f; dirty = true; }
            if (_config.Raid.SpawnRadius  < 5f)  { _config.Raid.SpawnRadius  = 5f;  dirty = true; }
            if (_config.Raid.AttackRadius < 3f)  { _config.Raid.AttackRadius = 3f;  dirty = true; }
            if (_config.Raid.EasyCount    < 1)   { _config.Raid.EasyCount    = 1;   dirty = true; }
            if (_config.Raid.NormalCount  < 1)   { _config.Raid.NormalCount  = 1;   dirty = true; }
            if (_config.Raid.HardCount    < 1)   { _config.Raid.HardCount    = 1;   dirty = true; }

            if (dirty) SaveConfig();
        }

        // ═══════════════════════════════════════════════════════════
        //  OXIDE HOOKS
        // ═══════════════════════════════════════════════════════════
        private void Init()
        {
            permission.RegisterPermission(PERM_USE,  this);
            permission.RegisterPermission(PERM_STOP, this);
        }

        private void Unload()
        {
            KillAllRaiders();
        }

        // Clean up from tracking set if an NPC dies naturally
        private void OnEntityDeath(ScientistNPC npc, HitInfo info)
        {
            if (npc != null)
                _activeRaiders.Remove(npc);
        }

        // ═══════════════════════════════════════════════════════════
        //  CHAT COMMANDS
        // ═══════════════════════════════════════════════════════════
        [ChatCommand("npcrraid")]
        private void CmdNpcRaid(BasePlayer player, string cmd, string[] args)
        {
            if (!HasPermission(player, PERM_USE)) return;

            string difficulty = (args.Length > 0) ? args[0].ToLower() : "normal";
            if (!IsValidDifficulty(difficulty))
            {
                SendReply(player, "Usage: /npcrraid [easy|normal|hard|boss]");
                return;
            }

            BuildingPrivlidge tc = GetRandomToolCupboard();
            if (tc == null)
            {
                SendReply(player, "<color=red>✖</color> No valid Tool Cupboards found on the map.");
                return;
            }

            SendReply(player,
                $"<color=orange>⚠ RAID INITIATED</color> — " +
                $"<color=yellow>{difficulty.ToUpper()}</color> squad inbound to <color=cyan>{GetGrid(tc.transform.position)}</color>");

            ExecuteStaggeredSpawn(tc.transform.position, difficulty);
        }

        [ChatCommand("npcrstop")]
        private void CmdNpcStop(BasePlayer player, string cmd, string[] args)
        {
            if (!HasPermission(player, PERM_STOP)) return;

            int count = KillAllRaiders();
            SendReply(player, $"<color=#ff4444>✖ TERMINATED:</color> {count} raider(s) removed.");
        }

        // ═══════════════════════════════════════════════════════════
        //  CONSOLE COMMANDS  (for RCON / server console use)
        // ═══════════════════════════════════════════════════════════
        [ConsoleCommand("npcrraid")]
        private void ConCmdNpcRaid(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null) return; // server console only

            string difficulty = arg.HasArgs() ? arg.GetString(0).ToLower() : "normal";
            if (!IsValidDifficulty(difficulty))
            {
                Puts("Usage: npcrraid [easy|normal|hard|boss]");
                return;
            }

            BuildingPrivlidge tc = GetRandomToolCupboard();
            if (tc == null) { Puts("No valid TCs found."); return; }

            Puts($"[NpcRaiders] Raid initiated at {GetGrid(tc.transform.position)} | Difficulty: {difficulty}");
            ExecuteStaggeredSpawn(tc.transform.position, difficulty);
        }

        [ConsoleCommand("npcrstop")]
        private void ConCmdNpcStop(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null) return;
            int count = KillAllRaiders();
            Puts($"[NpcRaiders] Terminated {count} raider(s).");
        }

        // ═══════════════════════════════════════════════════════════
        //  SPAWN LOGIC
        // ═══════════════════════════════════════════════════════════
        private void ExecuteStaggeredSpawn(Vector3 targetPos, string difficulty)
        {
            int  count   = GetRaiderCount(difficulty);
            bool hasBoss = (difficulty == "hard" || difficulty == "boss");

            // Fix: capture loop value with a local copy to avoid C# closure-in-loop bug
            for (int i = 0; i < count; i++)
            {
                int index = i;
                timer.Once(0.3f * index, () => SpawnRaider(targetPos, PREFAB_REGULAR, false, difficulty));
            }

            if (hasBoss)
                timer.Once(0.3f * (count + 1), () => SpawnRaider(targetPos, PREFAB_HEAVY, true, difficulty));
        }

        private void SpawnRaider(Vector3 target, string prefab, bool isBoss, string difficulty)
        {
            Vector3 spawnPos;
            if (!TryGetNavMeshPosition(target, out spawnPos))
            {
                PrintWarning("[NpcRaiders] Could not find a valid NavMesh position near TC — skipping raider.");
                return;
            }

            BaseEntity entity = GameManager.server.CreateEntity(prefab, spawnPos, Quaternion.identity);
            if (entity == null) return;

            ScientistNPC npc = entity as ScientistNPC;
            if (npc == null) { entity.Kill(); return; }

            npc.displayName = isBoss ? "ELITE COMMANDER" : "Raider";
            npc.Spawn();

            // Strip default loot and apply our loadout
            NextTick(() =>
            {
                if (npc == null || npc.IsDestroyed) return;

                npc.inventory.Strip();
                SetupLoadout(npc, isBoss, difficulty);

                // Equip first item in belt
                var firstItem = npc.inventory.containerBelt.GetSlot(0);
                if (firstItem != null)
                    npc.UpdateActiveItem(firstItem.uid);

                // Ensure NPC is standing, not crouching
                if (npc.modelState != null)
                {
                    npc.modelState.ducked = false;
                    npc.SendNetworkUpdate();
                }

                // Point navigator at the target
                if (npc.Brain?.Navigator != null)
                    npc.Brain.Navigator.SetDestination(target, BaseNavigator.NavigationSpeed.Fast);

                // Start repeating raid behavior loop
                InitializeRaidBehavior(npc, target);
            });

            // Register in tracking set and log spawn location
            _activeRaiders.Add(npc);
            Puts($"[NpcRaiders] Spawned {npc.displayName} at grid {GetGrid(spawnPos)} ({spawnPos.x:F1}, {spawnPos.y:F1}, {spawnPos.z:F1})");

            // Auto-despawn timer
            timer.Once(_config.Raid.DespawnTime, () =>
            {
                if (npc != null && !npc.IsDestroyed)
                {
                    _activeRaiders.Remove(npc);
                    npc.Kill();
                }
            });
        }

        // ═══════════════════════════════════════════════════════════
        //  RAID BEHAVIOR LOOP
        // ═══════════════════════════════════════════════════════════
        private void InitializeRaidBehavior(ScientistNPC npc, Vector3 target)
        {
            timer.Repeat(4f, 0, () =>
            {
                if (npc == null || npc.IsDestroyed || npc.IsDead())
                {
                    _activeRaiders.Remove(npc);
                    return;
                }

                // If the NPC has a player target in memory, let the AI handle it
                if (HasPlayerTarget(npc)) return;

                float dist = Vector3.Distance(npc.transform.position, target);

                if (dist > _config.Raid.AttackRadius)
                {
                    // Keep moving toward TC
                    if (npc.modelState != null && npc.modelState.ducked)
                    {
                        npc.modelState.ducked = false;
                        npc.SendNetworkUpdate();
                    }

                    if (npc.Brain?.Navigator != null)
                        npc.Brain.Navigator.SetDestination(target, BaseNavigator.NavigationSpeed.Fast);
                }
                else if (_config.Raid.AttackStructures)
                {
                    AttackNearbyStructure(npc);
                }
            });
        }

        // Directly trigger a melee attack on the closest damageable structure
        private void AttackNearbyStructure(ScientistNPC npc)
        {
            List<BaseCombatEntity> nearby = Facepunch.Pool.Get<List<BaseCombatEntity>>();
            Vis.Entities(npc.transform.position, _config.Raid.AttackRadius, nearby,
                LayerMask.GetMask("Construction", "Deployed"));

            BaseCombatEntity bestTarget = null;
            float bestDist = float.MaxValue;

            foreach (var ent in nearby)
            {
                if (ent == null || ent.IsDestroyed || ent.IsDead()) continue;
                if (!(ent is BuildingBlock || ent is Door || ent is StorageContainer)) continue;

                float d = Vector3.Distance(npc.transform.position, ent.transform.position);
                if (d < bestDist) { bestDist = d; bestTarget = ent; }
            }

            Facepunch.Pool.FreeUnmanaged(ref nearby);

            if (bestTarget == null) return;

            npc.SetAimDirection((bestTarget.transform.position - npc.transform.position).normalized);

            HitInfo hit = new HitInfo
            {
                Initiator   = npc,
                HitEntity   = bestTarget,
                damageTypes = new DamageTypeList()
            };
            hit.damageTypes.Add(DamageType.Explosion, 15f);
            bestTarget.OnAttacked(hit);
        }

        // ═══════════════════════════════════════════════════════════
        //  LOADOUT SETUP
        // ═══════════════════════════════════════════════════════════
        private void SetupLoadout(ScientistNPC npc, bool isBoss, string difficulty)
        {
            if (isBoss)
            {
                switch (difficulty)
                {
                    case "hard":
                        GiveItem(npc, "rifle.ak",       npc.inventory.containerBelt);
                        GiveItem(npc, "ammo.rifle",     npc.inventory.containerMain, 180);
                        GiveItem(npc, "coffeecan.helmet", npc.inventory.containerWear);
                        GiveItem(npc, "roadsign.jacket", npc.inventory.containerWear);
                        GiveItem(npc, "roadsign.kilt",  npc.inventory.containerWear);
                        npc.InitializeHealth(350f, 350f);
                        break;

                    case "boss":
                        GiveItem(npc, "lmg.m249",       npc.inventory.containerBelt);
                        GiveItem(npc, "ammo.rifle",     npc.inventory.containerMain, 500);
                        GiveItem(npc, "metal.facemask", npc.inventory.containerWear);
                        GiveItem(npc, "metal.plate.torso", npc.inventory.containerWear);
                        GiveItem(npc, "pants",          npc.inventory.containerWear);
                        npc.InitializeHealth(500f, 500f);
                        break;
                }
            }
            else
            {
                switch (difficulty)
                {
                    case "easy":
                        GiveItem(npc, "pistol.semiauto", npc.inventory.containerBelt);
                        GiveItem(npc, "ammo.pistol",    npc.inventory.containerMain, 64);
                        npc.InitializeHealth(80f, 80f);
                        break;

                    case "normal":
                        GiveItem(npc, "smg.mp5",        npc.inventory.containerBelt);
                        GiveItem(npc, "ammo.pistol",    npc.inventory.containerMain, 128);
                        GiveItem(npc, "hoodie",         npc.inventory.containerWear);
                        GiveItem(npc, "pants",          npc.inventory.containerWear);
                        npc.InitializeHealth(130f, 130f);
                        break;

                    case "hard":
                    case "boss":
                        GiveItem(npc, "rifle.ak",       npc.inventory.containerBelt);
                        GiveItem(npc, "ammo.rifle",     npc.inventory.containerMain, 180);
                        GiveItem(npc, "roadsign.jacket", npc.inventory.containerWear);
                        GiveItem(npc, "roadsign.kilt",  npc.inventory.containerWear);
                        npc.InitializeHealth(200f, 200f);
                        break;
                }
            }
        }

        private void GiveItem(ScientistNPC npc, string shortname, ItemContainer container, int amount = 1)
        {
            Item item = ItemManager.CreateByName(shortname, amount);
            if (item == null)
            {
                PrintWarning($"[NpcRaiders] Unknown item shortname: {shortname}");
                return;
            }
            if (!item.MoveToContainer(container))
                item.Remove();
        }

        // ═══════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════

        // Returns true if the NPC's AI memory contains a living player target
        private bool HasPlayerTarget(ScientistNPC npc)
        {
            if (npc?.Brain?.Senses?.Memory == null) return false;
            foreach (var target in npc.Brain.Senses.Memory.Targets)
            {
                if (target != null && !target.IsDestroyed)
                    return true;
            }
            return false;
        }

        private int KillAllRaiders()
        {
            int count = 0;
            foreach (var npc in _activeRaiders)
            {
                if (npc != null && !npc.IsDestroyed)
                {
                    npc.Kill();
                    count++;
                }
            }
            _activeRaiders.Clear();
            return count;
        }

        // Samples the NavMesh near the target so NPCs don't spawn inside rocks or water
        private bool TryGetNavMeshPosition(Vector3 center, out Vector3 result)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 rnd = UnityEngine.Random.insideUnitCircle * _config.Raid.SpawnRadius;
                Vector3 candidate = center + new Vector3(rnd.x, 0f, rnd.y);
                candidate.y = TerrainMeta.HeightMap.GetHeight(candidate) + 1f;

                NavMeshHit hit;
                if (NavMesh.SamplePosition(candidate, out hit, 5f, NavMesh.AllAreas))
                {
                    result = hit.position;
                    return true;
                }
            }

            result = Vector3.zero;
            return false;
        }

        private BuildingPrivlidge GetRandomToolCupboard()
        {
            var tcs   = UnityEngine.Object.FindObjectsOfType<BuildingPrivlidge>();
            var valid = Facepunch.Pool.Get<List<BuildingPrivlidge>>();

            foreach (var tc in tcs)
            {
                if (tc != null && !tc.IsDestroyed && tc.OwnerID != 0)
                    valid.Add(tc);
            }

            BuildingPrivlidge chosen = valid.Count == 0
                ? null
                : valid[UnityEngine.Random.Range(0, valid.Count)];

            Facepunch.Pool.FreeUnmanaged(ref valid);
            return chosen;
        }

        private string GetGrid(Vector3 pos)
        {
            float offset = ConVar.Server.worldsize / 2f;
            int col = Mathf.Clamp(Mathf.FloorToInt((pos.x + offset) / 146.3f), 0, 25);
            int row = Mathf.Clamp(Mathf.FloorToInt((offset - pos.z) / 146.3f), 0, 25);
            return $"{(char)('A' + col)}{row}";
        }

        private bool HasPermission(BasePlayer player, string perm)
        {
            if (permission.UserHasPermission(player.UserIDString, perm)) return true;
            if (player.net?.connection != null && player.net.connection.authLevel >= 2) return true;
            SendReply(player, "<color=red>✖ You don't have permission to use this command.</color>");
            return false;
        }

        private bool IsValidDifficulty(string d) =>
            d == "easy" || d == "normal" || d == "hard" || d == "boss";

        private int GetRaiderCount(string difficulty)
        {
            switch (difficulty)
            {
                case "easy":          return _config.Raid.EasyCount;
                case "hard":
                case "boss":          return _config.Raid.HardCount;
                default:              return _config.Raid.NormalCount;
            }
        }
    }
}
