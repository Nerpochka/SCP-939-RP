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
            _harmony.PatchAll();

            Log.Info("AmnesiaPatch939 запущен");
            base.OnEnabled();
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