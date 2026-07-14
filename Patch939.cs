using System;
using HarmonyLib;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp939;
using PlayerRoles.PlayableScps.Scp939.Ripples;

namespace AmnesiaPatch939
{
    // Отключение "сердцебиения"
    [HarmonyPatch(typeof(SurfaceRippleTrigger), "LateUpdate")]
    public static class HeartbeatPatch
    {
        public static bool Prefix() => false;
    }

    // Механика псевдо-противогаза
    [HarmonyPatch(typeof(Scp939AmnesticCloudInstance), nameof(Scp939AmnesticCloudInstance.OnEnter))]
    public static class AmnesiaPatch
    {
        public static bool Prefix(ReferenceHub player)
        {
            Team team = player.GetTeam();
            if (team == Team.ChaosInsurgency) return false;
            if (team == Team.FoundationForces && player.roleManager.CurrentRole.RoleTypeId != RoleTypeId.FacilityGuard) return false;
            return true;
        }
    }

    // Отключение "точек интереса"
    [HarmonyPatch(typeof(SpawnableRipplesTrigger), "OnSpawned")]
    public static class RipplePatch
    {
        public static bool Prefix() => false;
    }
}