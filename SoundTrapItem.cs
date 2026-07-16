using AdminToys;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Pickups.Projectiles;
using Exiled.API.Features.Spawn;
using Exiled.API.Features.Toys;
using Exiled.CustomItems.API.Features;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utils;

namespace AmnesiaPatch939
{
    public class SoundTrapItem : CustomItem
    {
        public override uint Id { get; set; } = 939001;
        public override string Name { get; set; } = "Звуковая ловушка";
        public override string Description { get; set; } = "Ловушка, которая оглушает SCP-939.";
        public override float Weight { get; set; } = 1f;
        public override ItemType Type { get; set; } = ItemType.Medkit;

        public override SpawnProperties SpawnProperties { get; set; } = new SpawnProperties
        {
            Limit = 2,
            DynamicSpawnPoints = new List<DynamicSpawnPoint>
    {
        new DynamicSpawnPoint
        {
            Chance = 100,
            Location = SpawnLocationType.InsideHczArmory,
        },
        new DynamicSpawnPoint
        {
            Chance = 100,
            Location = SpawnLocationType.InsideHczArmory, // Повторяем точку для второго спавна
        }
    }
        };

        // setting trap
        private const float TriggerRadius = 5f; 
        private const float ExplosionRadius = 6f;
        private const int ExplosionCount = 8;
        private const float ExplosionInterval = 0.4f;
        private const float ExplosionDamageScp939 = 225f;
        private const float ExplosionDamageHuman = 9f;

        private static readonly RoomType[] AllowedRooms =
      {
            RoomType.LczGlassBox,
            RoomType.EzGateA,
            RoomType.EzGateB
        };

        private static bool _isTrapActive;

        protected override void OnPickingUp(Exiled.Events.EventArgs.Player.PickingUpItemEventArgs ev)
        {
            ev.IsAllowed = false;
            ev.Pickup.Destroy();
            this.Give(ev.Player);

            string customUi = "<align=left>" +
                              "<margin-left=-60%><sprite name=\"Medkit\"> <color=yellow>Звуковая ловушка</color>\n" +
                              "<margin-left=-60%><size=18>Ловушка, которая оглушает SCP-939.</size>\n\n\n\n";

            ev.Player.Broadcast(3, customUi);
        }

        private class ActivePrimitive
        {
            public Primitive PrimitiveObject;
            public Vector3 LocalPosToRoot;
            public Quaternion LocalRotToRoot;
            public bool IsLidPart;
        }

        protected override void SubscribeEvents()
        {
            Exiled.Events.Handlers.Player.UsingItem += OnUsingItem;
            Exiled.Events.Handlers.Server.RoundStarted += OnRoundStarted;
            base.SubscribeEvents();
        }

        protected override void UnsubscribeEvents()
        {
            Exiled.Events.Handlers.Player.UsingItem -= OnUsingItem;
            Exiled.Events.Handlers.Server.RoundStarted -= OnRoundStarted;
            base.UnsubscribeEvents();
        }

        private void OnRoundStarted()
        {
            _isTrapActive = false;
        }

        private void OnUsingItem(UsingItemEventArgs ev)
        {
            if (!Check(ev.Player.CurrentItem)) return;
            ev.IsAllowed = false;

            if (ev.Player.Role.Team != Team.FoundationForces)
            {
                ev.Player.ShowHint("<color=red>Доступно только СБ/NTF.</color>", 3f);
                return;
            }

            RoomType currentRoomType = ev.Player.CurrentRoom?.Type ?? RoomType.Unknown;
            if (!AllowedRooms.Contains(currentRoomType))
            {
                ev.Player.ShowHint("<color=red>Ловушку можно ставить только в Glassroom, Gate A или Gate B.</color>", 3f);
                return;
            }

            if (_isTrapActive)
            {
                ev.Player.ShowHint("<color=red>Ловушка была установлена.</color>", 3f);
                return;
            }

            _isTrapActive = true;

            TryPlaceTrap(ev.Player);
        }

        private void TryPlaceTrap(Player player)
        {
            RoomType currentRoom = player.CurrentRoom?.Type ?? RoomType.Unknown;
            Vector3 spawnPosition = FindFloorPosition(player);

            player.ShowHint("<color=yellow>Установка</color>", 1.5f);
            var currentItem = player.CurrentItem;

            Timing.CallDelayed(1.5f, () =>
            {
                if (player == null || player.CurrentItem != currentItem)
                {
                    _isTrapActive = false;
                    return;
                }

                RoomType roomAtPlacement = player.CurrentRoom?.Type ?? RoomType.Unknown;
                if (!AllowedRooms.Contains(roomAtPlacement))
                {
                    player.ShowHint("<color=red>Установка отменена.</color>", 3f);
                    _isTrapActive = false;
                    return;
                }

                List<ActivePrimitive> activePrimitives = new List<ActivePrimitive>();
                Quaternion spawnRotation = Quaternion.Euler(0f, player.Rotation.eulerAngles.y, 0f);

                SpawnComplexTrapModel(spawnPosition, spawnRotation, activePrimitives);
                player.RemoveItem(currentItem);
                player.ShowHint("<color=green>Успешно</color>", 3f);

                Timing.RunCoroutine(AnimateTrapOpening(spawnPosition, spawnRotation, activePrimitives));
                Timing.RunCoroutine(MonitorTrap(spawnPosition, currentRoom, activePrimitives));
            });
        }

        private IEnumerator<float> MonitorTrap(Vector3 position, RoomType roomType, List<ActivePrimitive> activePrimitives)
        {
            while (true)
            {
                var allScp939 = Player.List
                    .Where(p => p.Role.Type == RoleTypeId.Scp939 && p.IsAlive)
                    .ToList();

                Player targetToTrigger = null;

                foreach (var scp939 in allScp939)
                {
                    float distance = Vector3.Distance(position, scp939.Position);

                    if (distance <= TriggerRadius)
                    {
                        targetToTrigger = scp939;
                        break;
                    }
                    else
                    {
                        scp939.Broadcast(2, $"Проследуйте в комнату: <color=red>{roomType}</color>\n              {Mathf.RoundToInt(distance)} метров");

                    }
                }

                if (targetToTrigger != null)
                {
                    Timing.RunCoroutine(ExplosionSequence(targetToTrigger, position, activePrimitives));
                    yield break;
                }

                yield return Timing.WaitForSeconds(2f);
            }
        }

        private IEnumerator<float> ExplosionSequence(Player victim, Vector3 position, List<ActivePrimitive> activePrimitives)
        {
            if (victim != null && victim.IsAlive && victim.Role.Type == RoleTypeId.Scp939)
                victim.EnableEffect(EffectType.Ensnared, ExplosionCount * ExplosionInterval, true);

            for (int i = 0; i < ExplosionCount; i++)
            {
                ExplosionUtils.ServerSpawnEffect(position, ItemType.GrenadeHE);
                foreach (Player target in Player.List.Where(p => p.IsAlive && Vector3.Distance(position, p.Position) <= ExplosionRadius))
                {
                    if (target.Role.Type == RoleTypeId.Scp939)
                    {
                        target.Hurt(ExplosionDamageScp939, DamageType.Explosion);
                    }
                    else if (target.Role.Team != Team.SCPs)
                    {
                        target.Hurt(ExplosionDamageHuman, DamageType.Explosion);
                    }
                }

                yield return Timing.WaitForSeconds(ExplosionInterval);
            }

            var scp939sInRadius = Player.List
                .Where(p => p.IsAlive && p.Role.Type == RoleTypeId.Scp939 && Vector3.Distance(position, p.Position) <= ExplosionRadius);

            foreach (Player scp in scp939sInRadius)
            {  
                scp.Kill("Сильнейшая контузия");
            }

            CleanupTrap(activePrimitives);
        }





        private void CleanupTrap(List<ActivePrimitive> activePrimitives)
        {
            foreach (var activePrim in activePrimitives)
            {
                activePrim.PrimitiveObject?.Destroy();
            }
            activePrimitives.Clear();

            _isTrapActive = false;
        }

        private Vector3 FindFloorPosition(Player player)
        {
            Vector3 origin = player.Position + Vector3.up * 2f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 6f);
            if (hits.Length > 0)
            {
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit h in hits)
                {
                    if (h.collider.transform.root == player.GameObject.transform.root) continue;
                    return h.point;
                }
            }
            return player.Position;
        }

        private void SpawnComplexTrapModel(Vector3 rootPosition, Quaternion rootRotation, List<ActivePrimitive> activePrimitives)
        {
            var blocks = GetTrapBlocksData();
            Vector3 cryshkaPos = new Vector3(-0.0104666f, 0.171f, -0.055f);
            Quaternion cryshkaRot = Quaternion.identity;

            foreach (var block in blocks)
            {
                if (block.BlockType == 1)
                {
                    PrimitiveType primType = (PrimitiveType)block.PrimitiveType;
                    Color blockColor = Color.white;
                    if (ColorUtility.TryParseHtmlString("#" + block.ColorHex, out Color parsedColor)) blockColor = parsedColor;

                    Vector3 localPos;
                    Quaternion localRot;
                    bool isLid;

                    if (block.ParentId == -4160618)
                    {
                        localPos = cryshkaPos + (cryshkaRot * block.Position);
                        localRot = cryshkaRot * Quaternion.Euler(block.Rotation);
                        isLid = true;
                    }
                    else
                    {
                        localPos = block.Position;
                        localRot = Quaternion.Euler(block.Rotation);
                        isLid = false;
                    }

                    Vector3 worldPos = rootPosition + (rootRotation * localPos);
                    Quaternion worldRot = rootRotation * localRot;
                    Vector3 absoluteScale = new Vector3(
                        Mathf.Abs(block.Scale.x),
                        Mathf.Abs(block.Scale.y),
                        Mathf.Abs(block.Scale.z)
                    );

                    PrimitiveFlags flags = PrimitiveFlags.Visible;
                    if (block.Name == "cas") flags |= PrimitiveFlags.Collidable;
                    Primitive primitive = Primitive.Create(primType, flags, worldPos, worldRot.eulerAngles, absoluteScale, false, blockColor);
                    primitive.Spawn();
                    activePrimitives.Add(new ActivePrimitive { PrimitiveObject = primitive, LocalPosToRoot = localPos, LocalRotToRoot = localRot, IsLidPart = isLid });
                }
            }
        }

        private IEnumerator<float> AnimateTrapOpening(Vector3 rootPosition, Quaternion rootRotation, List<ActivePrimitive> activePrimitives)
        {
            Vector3 pivotLocal = new Vector3(-0.0104666f, 0.1852511f, 0.296632877f);
            float duration = 1.2f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Timing.DeltaTime;
                float progress = elapsed / duration;
                float currentAngle = Mathf.Lerp(0f, 105f, progress);
                Quaternion relativeRotation = Quaternion.Euler(currentAngle, 0f, 0f);

                foreach (var activePrim in activePrimitives)
                {
                    if (activePrim.PrimitiveObject == null || !activePrim.IsLidPart) continue;
                    Vector3 animLocalPos = pivotLocal + relativeRotation * (activePrim.LocalPosToRoot - pivotLocal);
                    Quaternion animLocalRot = relativeRotation * activePrim.LocalRotToRoot;
                    activePrim.PrimitiveObject.Position = rootPosition + (rootRotation * animLocalPos);
                    activePrim.PrimitiveObject.Rotation = rootRotation * animLocalRot;
                }
                yield return Timing.WaitForOneFrame;
            }
        }

        #region Данные модели
        private class TrapBlock { public string Name; public long ObjectId; public long ParentId; public Vector3 Position; public Vector3 Rotation; public Vector3 Scale; public int BlockType; public int PrimitiveType; public string ColorHex; }

        private List<TrapBlock> GetTrapBlocksData()
        {
            return new List<TrapBlock>
    {
        new TrapBlock { Name = "cas", ObjectId = -20108, ParentId = -19888, Position = new Vector3(0f, 0.074f, 0f), Rotation = new Vector3(0f, 0f, 0f), Scale = new Vector3(0.7226f, -0.182f, 0.5813f), BlockType = 1, PrimitiveType = 3, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder", ObjectId = -33828, ParentId = -19888, Position = new Vector3(0.333f, -0.014f, 0f), Rotation = new Vector3(0f, 90f, 90f), Scale = new Vector3(0.054f, 0.29f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (1)", ObjectId = -42706, ParentId = -19888, Position = new Vector3(-0.333f, -0.014f, 0f), Rotation = new Vector3(0f, 90f, 90f), Scale = new Vector3(0.054f, 0.29f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (2)", ObjectId = -45314, ParentId = -19888, Position = new Vector3(0f, -0.011f, -0.261f), Rotation = new Vector3(0f, 180f, 90f), Scale = new Vector3(0.054f, 0.342f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (3)", ObjectId = -55866, ParentId = -19888, Position = new Vector3(0f, -0.012f, 0.262f), Rotation = new Vector3(0f, 180f, 90f), Scale = new Vector3(0.054f, 0.342f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "ugol (1)", ObjectId = -1442730, ParentId = -19888, Position = new Vector3(0.365f, 0.175f, 0f), Rotation = new Vector3(0f, 0f, 0f), Scale = new Vector3(0.0085f, -0.02f, 0.585f), BlockType = 1, PrimitiveType = 3, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "ugol (2)", ObjectId = -1447902, ParentId = -19888, Position = new Vector3(-0.365f, 0.175f, 0f), Rotation = new Vector3(0f, 0f, 0f), Scale = new Vector3(0.0085f, -0.02f, 0.585f), BlockType = 1, PrimitiveType = 3, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "ugol (3)", ObjectId = -4087188, ParentId = -19888, Position = new Vector3(0f, 0.175f, -0.295f), Rotation = new Vector3(0f, 270f, 0f), Scale = new Vector3(0.0085f, -0.02f, 0.725f), BlockType = 1, PrimitiveType = 3, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "ugol (4)", ObjectId = -4100634, ParentId = -19888, Position = new Vector3(0f, 0.175f, 0.295f), Rotation = new Vector3(0f, 270f, 0f), Scale = new Vector3(0.0085f, -0.02f, 0.725f), BlockType = 1, PrimitiveType = 3, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "Cylinder (10)", ObjectId = -4114762, ParentId = -19888, Position = new Vector3(-0.202f, 0.185f, 0.297f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.015f, 0.028f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FFFFFFFF" },
        new TrapBlock { Name = "Cylinder", ObjectId = -4090296, ParentId = -19888, Position = new Vector3(0.361f, 0.175f, -0.291f), Rotation = new Vector3(0f, 0f, 0f), Scale = new Vector3(0.015f, 0.01f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "Cylinder (1)", ObjectId = -4096050, ParentId = -19888, Position = new Vector3(-0.362f, 0.175f, -0.291f), Rotation = new Vector3(0f, 0f, 0f), Scale = new Vector3(0.015f, 0.01f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "Cylinder (2)", ObjectId = -4100088, ParentId = -19888, Position = new Vector3(-0.362f, 0.175f, 0.291f), Rotation = new Vector3(0f, 0f, 0f), Scale = new Vector3(0.015f, 0.01f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "Cylinder (6)", ObjectId = -4111500, ParentId = -19888, Position = new Vector3(0.201f, 0.185f, 0.297f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.015f, 0.028f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FFFFFFFF" },
        new TrapBlock { Name = "Cylinder (7)", ObjectId = -4111486, ParentId = -19888, Position = new Vector3(0.275f, 0.185f, 0.297f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.015f, 0.028f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FFFFFFFF" },
        new TrapBlock { Name = "Cylinder (3)", ObjectId = -4100102, ParentId = -19888, Position = new Vector3(0.361f, 0.175f, 0.291f), Rotation = new Vector3(0f, 0f, 0f), Scale = new Vector3(0.015f, 0.01f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FF0000FF" },
        new TrapBlock { Name = "Cylinder (9)", ObjectId = -4114748, ParentId = -19888, Position = new Vector3(-0.275f, 0.185f, 0.297f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.015f, 0.028f, 0.015f), BlockType = 1, PrimitiveType = 2, ColorHex = "FFFFFFFF" },
        new TrapBlock { Name = "Cube (1)", ObjectId = -4176968, ParentId = -19888, Position = new Vector3(0.006f, 0.157f, 0.075f), Rotation = new Vector3(90f, 0f, 0f), Scale = new Vector3(0.572f, 0.242f, 0.017f), BlockType = 1, PrimitiveType = 3, ColorHex = "2E2E2EFF" },
        new TrapBlock { Name = "Cube (2)", ObjectId = -4179736, ParentId = -19888, Position = new Vector3(0.002f, 0.157f, -0.174f), Rotation = new Vector3(90f, 0f, 0f), Scale = new Vector3(0.201f, 0.105f, 0.017f), BlockType = 1, PrimitiveType = 3, ColorHex = "2E2E2EFF" },
        new TrapBlock { Name = "Cube (3)", ObjectId = -4180836, ParentId = -19888, Position = new Vector3(0.001f, 0.159f, -0.21f), Rotation = new Vector3(90f, 0f, 0f), Scale = new Vector3(0.007f, 0.033f, 0.017f), BlockType = 1, PrimitiveType = 3, ColorHex = "131313FF" },
        new TrapBlock { Name = "kryshka (1)", ObjectId = -4119602, ParentId = -4160618, Position = new Vector3(0.012f, 0.028f, 0.056f), Rotation = Vector3.zero, Scale = new Vector3(0.723f, -0.058f, 0.576f), BlockType = 1, PrimitiveType = 3, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (4)", ObjectId = -4121424, ParentId = -4160618, Position = new Vector3(0.345f, 0.055f, 0.06f), Rotation = new Vector3(0f, 90f, 270f), Scale = new Vector3(0.054f, 0.263f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (5)", ObjectId = -4125992, ParentId = -4160618, Position = new Vector3(-0.321f, 0.055f, 0.058f), Rotation = new Vector3(0f, 90f, 270f), Scale = new Vector3(0.054f, 0.263f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (7)", ObjectId = -4129022, ParentId = -4160618, Position = new Vector3(0.012f, 0.055f, 0.321f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.054f, 0.361f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "kryshka (2)", ObjectId = -4157538, ParentId = -4160618, Position = new Vector3(0.014f, 0.06f, 0.063f), Rotation = Vector3.zero, Scale = new Vector3(0.678f, -0.039f, 0.526f), BlockType = 1, PrimitiveType = 3, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (8)", ObjectId = -4157518, ParentId = -4160618, Position = new Vector3(0.012f, 0.055f, -0.203f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.054f, 0.361f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cube", ObjectId = -4169536, ParentId = -4160618, Position = new Vector3(0.013f, 0.011f, 0.058f), Rotation = new Vector3(270f, 0f, 0f), Scale = new Vector3(0.573f, 0.438f, 0.029f), BlockType = 1, PrimitiveType = 3, ColorHex = "000000FF" },
        new TrapBlock { Name = "Cylinder (6)", ObjectId = -4173506, ParentId = -4160618, Position = new Vector3(-0.321f, 0f, 0.058f), Rotation = new Vector3(0f, 90f, 270f), Scale = new Vector3(0.05f, 0.263f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (9)", ObjectId = -4173852, ParentId = -4160618, Position = new Vector3(0.012f, 0f, 0.321f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.054f, 0.361f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (10)", ObjectId = -4174482, ParentId = -4160618, Position = new Vector3(0.345f, 0f, 0.06f), Rotation = new Vector3(0f, 90f, 270f), Scale = new Vector3(0.054f, 0.263f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" },
        new TrapBlock { Name = "Cylinder (11)", ObjectId = -4174844, ParentId = -4160618, Position = new Vector3(0.012f, 0f, -0.203f), Rotation = new Vector3(0f, 0f, 270f), Scale = new Vector3(0.054f, 0.361f, 0.057f), BlockType = 1, PrimitiveType = 2, ColorHex = "544D4DFF" }
    };
        }
        #endregion
    }
}