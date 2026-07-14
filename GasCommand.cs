using System;
using System.Collections.Generic;
using System.Reflection;
using CommandSystem;
using Exiled.API.Enums;
using Exiled.API.Features;
using HarmonyLib;
using Mirror;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp939;
using RelativePositioning;
using UnityEngine;

namespace AmnesiaPatch939
{
    [CommandHandler(typeof(ClientCommandHandler))]
    public class GasCommand : ICommand
    {
        public string Command => "gas";
        public string[] Aliases => new string[] { "cloud" };
        public string Description => "Кастомная абилка газа для SCP-939";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            Player player = Player.Get(sender);
            if (player == null) { response = "Вы должны быть игроком!"; return false; }
            if (player.Role.Type != RoleTypeId.Scp939) { response = "Доступно только для SCP-939!"; return false; }

            if (Plugin.Instance.Cooldowns.TryGetValue(player.UserId, out float expireTime) && Time.time < expireTime)
            {
                int timeLeft = Mathf.CeilToInt(expireTime - Time.time);
                player.ShowHint($"<color=red>Газ перезаряжается! Осталось: {timeLeft} сек.</color>", 3f);
                response = $"Кулдаун {timeLeft} сек.";
                return false;
            }

            if (Plugin.Instance.ActiveCasts.TryGetValue(player.UserId, out float startTime))
            {
                float holdTime = Time.time - startTime;
                Plugin.Instance.ActiveCasts.Remove(player.UserId);
                Plugin.Instance.PendingRelease[player.UserId] = holdTime;
                response = "Газ выпускается досрочно.";
                return true;
            }

            Plugin.Instance.ActiveCasts[player.UserId] = Time.time;
            Plugin.Instance.PendingStart.Add(player.UserId);
            response = "Зарядка газа началась.";
            return true;
        }
    }

    public class GasServerHandler : MonoBehaviour
    {
        private readonly Dictionary<string, float> _chargeStart = new Dictionary<string, float>();

        // Вызывается каждый кадр движком Unity автоматически
        private void Update()
        {
            if (Plugin.Instance == null) return;

            float now = Time.time;

            // Старт зарядки
            foreach (string userId in new List<string>(Plugin.Instance.PendingStart))
            {
                Plugin.Instance.PendingStart.Remove(userId);
                Player player = Player.Get(userId);
                if (player == null || player.Role.Type != RoleTypeId.Scp939) continue;

                _chargeStart[userId] = now;
                player.EnableEffect(EffectType.Disabled, 4f); // Даем легкое замедление/стопор на каст
            }

            // Накопление (Шкала в UI)
            foreach (var kvp in new Dictionary<string, float>(_chargeStart))
            {
                string userId = kvp.Key;
                float elapsed = now - kvp.Value;
                Player player = Player.Get(userId);

                if (player == null || player.Role.Type != RoleTypeId.Scp939 || !Plugin.Instance.ActiveCasts.ContainsKey(userId))
                {
                    _chargeStart.Remove(userId);
                    if (Plugin.Instance.ActiveCasts.ContainsKey(userId)) Plugin.Instance.ActiveCasts.Remove(userId);
                    continue;
                }

                int blocks = Mathf.Clamp(Mathf.FloorToInt((elapsed / 3.0f) * 10), 0, 10);
                string bar = new string('█', blocks) + new string('░', 10 - blocks);
                string color = elapsed < 1.5f ? "yellow" : "green";
                player.ShowHint($"<color=white>Накопление газа:</color>\n<color={color}>[{bar}] {(elapsed / 3.0f * 100):F0}%</color>\n<color=gray>Нажмите повторно для выпуска</color>", 0.25f);

                if (elapsed >= 3.0f)
                {
                    _chargeStart.Remove(userId);
                    Plugin.Instance.ActiveCasts.Remove(userId);
                    Plugin.Instance.PendingRelease[userId] = 3.0f;
                }
            }

            // Физический спавн облака без лишних анимаций собаки
            foreach (var kvp in new Dictionary<string, float>(Plugin.Instance.PendingRelease))
            {
                Plugin.Instance.PendingRelease.Remove(kvp.Key);
                _chargeStart.Remove(kvp.Key);

                Player player = Player.Get(kvp.Key);
                if (player == null || player.Role.Type != RoleTypeId.Scp939) continue;

                player.DisableEffect(EffectType.Disabled);

                Scp939AmnesticCloudAbility cloudAbility = GetCloudAbility(player);
                if (cloudAbility == null) continue;

                float holdTime = kvp.Value;
                float progress = Mathf.Clamp01(holdTime / 3.0f);

                var prefabField = AccessTools.Field(typeof(Scp939AmnesticCloudAbility), "_instancePrefab");
                if (prefabField == null) continue;

                var prefab = prefabField.GetValue(cloudAbility) as Scp939AmnesticCloudInstance;
                if (prefab == null) continue;

                Vector3 spawnPos = player.ReferenceHub.PlayerCameraReference.position;

                var instance = UnityEngine.Object.Instantiate(prefab);
                instance.transform.position = spawnPos;

                instance.Network_syncOwner = player.ReferenceHub.netId;
                instance.Network_syncPos = new RelativePosition(spawnPos);
                instance.Network_syncHoldTime = (byte)Mathf.RoundToInt(progress * 255f);

                instance.ServerSetup(player.ReferenceHub);
                Traverse.Create(instance).Method("SetAbilityCache").GetValue();

                float cloudDuration = Mathf.Lerp(5f, 60f, progress);
                AccessTools.Field(typeof(Scp939AmnesticCloudInstance), "_targetDuration")?.SetValue(instance, cloudDuration);
                instance.State = Scp939AmnesticCloudInstance.CloudState.Created;

                instance.MaxDistance = Mathf.Lerp(2.25f, 7.5f, progress);

                float scale = Mathf.Lerp(0.4f, 1.6f, progress);
                instance.transform.localScale = new Vector3(scale, scale, scale);

                NetworkServer.Spawn(instance.gameObject);

                int percent = Mathf.RoundToInt(progress * 100f);
                string sizeLabel = progress < 0.5f ? "<color=white>небольшое</color>" : "<color=white>большое</color>";
                player.ShowHint($"<color=white>Выпущено {sizeLabel} облако газа! ({percent}%)</color>", 3f);

                Plugin.Instance.Cooldowns[player.UserId] = now + Plugin.Instance.Config.CustomCloudCooldown;
            }
        }

        private Scp939AmnesticCloudAbility GetCloudAbility(Player player)
        {
            try
            {
                var subroutineProperty = player.Role.Base.GetType().GetProperty("SubroutineModule", BindingFlags.Public | BindingFlags.Instance);
                if (subroutineProperty == null) return null;
                var module = subroutineProperty.GetValue(player.Role.Base);
                if (module == null) return null;
                var tryGetMethod = module.GetType().GetMethod("TryGetSubroutine");
                if (tryGetMethod == null) return null;
                var genericMethod = tryGetMethod.MakeGenericMethod(typeof(Scp939AmnesticCloudAbility));
                object[] parameters = new object[] { null };
                bool success = (bool)genericMethod.Invoke(module, parameters);
                return success ? parameters[0] as Scp939AmnesticCloudAbility : null;
            }
            catch { return null; }
        }
    }
}