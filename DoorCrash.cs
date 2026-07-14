using HarmonyLib;
using PlayerRoles.PlayableScps.Scp939;
using Exiled.API.Features;
using Exiled.API.Features.Doors;
using Interactables.Interobjects.DoorUtils;
using System.Linq;
using UnityEngine;
using Mirror;

namespace AmnesiaPatch939
{
    [HarmonyPatch(typeof(Scp939LungeAbility), "OnGrounded")]
    public static class LungeDoorBreakPatch
    {
        public static void Postfix(Scp939LungeAbility __instance)
        {
            if (!NetworkServer.active) return;

            if (__instance.State != Scp939LungeState.LandHarsh && __instance.State != Scp939LungeState.LandRegular)
                return;

            ReferenceHub owner = __instance.Owner;
            if (owner == null) return;

            Player player = Player.Get(owner);
            if (player == null) return;

            TryBreakNearbyDoor(player);
        }

        private static void TryBreakNearbyDoor(Player scp939)
        {
            if (UnityEngine.Random.Range(0, 100) >= 30) return;

            Vector3 landingPos = scp939.Position;
            // Вы можете менять радиус поиска двери после преземления, 
            // но лучше не трогать.
            const float radius = 2.0f;

            Door nearbyDoor = Door.List
                .Where(d => d.Base is Interactables.Interobjects.BreakableDoor)
                .Where(d => !((BreakableDoor)d).IsDestroyed)
                .Where(d => (d.Position - landingPos).sqrMagnitude <= radius * radius)
                .OrderBy(d => (d.Position - landingPos).sqrMagnitude)
                .FirstOrDefault();

            if (nearbyDoor is BreakableDoor breakable)
            {
                breakable.Break(DoorDamageType.Grenade);
            }
        }
    }
}