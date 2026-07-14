using System;
using System.Collections.Generic;
using Exiled.API.Features;
using HarmonyLib;
using UnityEngine;

namespace AmnesiaPatch939
{
    public class Plugin : Plugin<Config>
    {
        public override string Name => "Patch939";
        public override string Author => "SairwX";
        public override Version Version => new Version(1, 0, 0);
        public override Version RequiredExiledVersion => new Version(8, 0, 0);

        public static Plugin Instance { get; private set; }
        private Harmony _harmony;
        private GameObject _handlerObject;

        public Dictionary<string, float> ActiveCasts { get; set; } = new Dictionary<string, float>();
        public Dictionary<string, float> Cooldowns { get; set; } = new Dictionary<string, float>();
        public List<string> PendingStart { get; set; } = new List<string>();
        public Dictionary<string, float> PendingRelease { get; set; } = new Dictionary<string, float>();

        public override void OnEnabled()
        {
            Instance = this;
            _handlerObject = new GameObject("GasServerHandlerObject");
            _handlerObject.AddComponent<GasServerHandler>();
            UnityEngine.Object.DontDestroyOnLoad(_handlerObject);

            // Статичный ID
            _harmony = new Harmony("amnesiapatch939");

            try
            {
                _harmony.PatchAll();
            }
            catch (Exception ex)
            {
                Log.Error($"[AmnesiaPatch939] Ошибка при применении патчей: {ex}");
            }

            // проверка патчей
            VerifyPatches();

            Log.Info("AmnesiaPatch939 запущен");
            base.OnEnabled();
        }

        private void VerifyPatches()
        {
            var expected = new (Type type, string method)[]
            {
                (typeof(PlayerRoles.PlayableScps.Scp939.Ripples.SurfaceRippleTrigger), "LateUpdate"),
                (typeof(PlayerRoles.PlayableScps.Scp939.Scp939AmnesticCloudInstance), "OnEnter"),
                (typeof(PlayerRoles.PlayableScps.Scp939.Ripples.SpawnableRipplesTrigger), "OnSpawned"),
            };

            var patchedMethods = new HashSet<System.Reflection.MethodBase>(Harmony.GetAllPatchedMethods());

            foreach (var (type, methodName) in expected)
            {
                var method = AccessTools.Method(type, methodName);
                if (method == null)
                {
                    Log.Error($"[AmnesiaPatch939] Метод {type.FullName}.{methodName} не найден — возможно, изменилась сигнатура/имя в текущей версии игры.");
                    continue;
                }

                if (patchedMethods.Contains(method))
                {
                    Log.Info($"[AmnesiaPatch939] Патч успешно применён: {type.Name}.{methodName}");
                }
                else
                {
                    Log.Warn($"[AmnesiaPatch939] Патч НЕ применён: {type.Name}.{methodName} (метод найден, но не в списке пропатченных).");
                }
            }
        }

        public override void OnDisabled()
        {
            if (_handlerObject != null)
            {
                UnityEngine.Object.Destroy(_handlerObject);
                _handlerObject = null;
            }

            _harmony?.UnpatchAll(_harmony.Id);
            _harmony = null;

            ActiveCasts.Clear();
            Cooldowns.Clear();
            PendingStart.Clear();
            PendingRelease.Clear();

            Instance = null;
            base.OnDisabled();
        }
    }

    public class Config : Exiled.API.Interfaces.IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;
        public float CustomCloudCooldown { get; set; } = 20f;
    }
}