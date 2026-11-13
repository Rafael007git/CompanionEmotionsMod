using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CompanionEmotionsMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.lorifel007.furnitureanimations", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aedenthorn.Tattoos", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("She Will Punish Them.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lorifel007.companionemotions";
        public const string PluginName = "Companion Emotions Mod";
        public const string PluginVersion = "0.1.5";

        public static ManualLogSource Log;
        private Harmony harmony;

        public struct SavedEmotionState
        {
            public float Embarrassment;
            public float Arousal;
        }

        public static readonly Dictionary<string, SavedEmotionState> EmotionMemory =
            new Dictionary<string, SavedEmotionState>();

        // Config entries
        public static ConfigEntry<bool> IncludePlayerCharacter;
        public static ConfigEntry<bool> ShowDebugOverlay;
        public static ConfigEntry<KeyCode> ToggleDebugKey;
        public static ConfigEntry<float> BaseGainSpeed;
        public static ConfigEntry<float> BaseDecaySpeed;
        public static ConfigEntry<Color> BlushColorConfig;
        public static ConfigEntry<float> MaxBlushAlpha;
        public static ConfigEntry<float> SweatGlossBoost;
        public static ConfigEntry<float> MinPeakSweatGloss;
        public static ConfigEntry<bool> EnableCastleLifeFix;

        // Loaded assets
        public static Texture2D BlushTexture;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"[{PluginName}] Initializing v{PluginVersion}...");

            try
            {
                InitConfig();
                LoadAssets();

                harmony = new Harmony(PluginGuid);
                harmony.PatchAll();

                Log.LogInfo($"[{PluginName}] Successfully loaded and patched!");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[{PluginName}] Failed to initialize: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void InitConfig()
        {
            IncludePlayerCharacter = Config.Bind(
                "General Settings",
                "Include Player Character",
                true,
                "True: enable emotional reactions and ENF poses for the main player character. False: companions only."
            );

            ShowDebugOverlay = Config.Bind(
                "Debug Settings",
                "Show Debug Overlay",
                false,
                "Show the diagnostic emotion overlay above companions."
            );

            ToggleDebugKey = Config.Bind(
                "Debug Settings",
                "Toggle Overlay Key",
                KeyCode.F8,
                "Key to toggle the debug overlay on/off."
            );

            BaseGainSpeed = Config.Bind(
                "Balance Settings",
                "Base Gain Speed",
                25f,
                "Base speed of embarrassment increase per second."
            );

            BaseDecaySpeed = Config.Bind(
                "Balance Settings",
                "Base Decay Speed",
                15f,
                "Base speed of embarrassment decrease per second."
            );

            BlushColorConfig = Config.Bind(
                "Visual Settings",
                "Blush Color",
                new Color(0.76f, 0.16f, 0.22f, 1f),
                "Tint color for the dynamic cheek blush decal."
            );

            MaxBlushAlpha = Config.Bind(
                "Visual Settings",
                "Max Blush Intensity",
                0.95f,
                "Maximum alpha transparency (0.0 to 1.0) of the blush decal at 100 Embarrassment."
            );

            SweatGlossBoost = Config.Bind(
                "Visual Settings",
                "Sweat Gloss Boost",
                0.30f,
                "Additional skin smoothness added at 100% emotional stress."
            );

            MinPeakSweatGloss = Config.Bind(
                "Visual Settings",
                "Min Peak Sweat Gloss",
                0.12f,
                "Minimum target skin smoothness at 100% stress (ensures visible sweat sheen even on dry skin presets)."
            );

            EnableCastleLifeFix = Config.Bind(
                "General Settings",
                "Enable Castle Life Fix",
                true,
                "Fixes companion animation freeze after Free Pose Mode and restores autonomous castle behavior."
            );

        }

        private void LoadAssets()
        {
            string modFolder = Path.Combine(Paths.PluginPath, "CompanionEmotionsMod");
            string texturesFolder = Path.Combine(modFolder, "textures");
            string posesFolder = Path.Combine(modFolder, "poses");

            if (!Directory.Exists(texturesFolder))
                Directory.CreateDirectory(texturesFolder);

            string blushPath = Path.Combine(texturesFolder, "face_blush.png");
            if (File.Exists(blushPath))
            {
                BlushTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                BlushTexture.LoadImage(File.ReadAllBytes(blushPath));
                Log.LogInfo($"[{PluginName}] Successfully loaded face_blush.png from: {blushPath}");
            }
            else
            {
                Log.LogWarning($"[{PluginName}] face_blush.png not found at: {blushPath}");
            }

            PoseLoader.LoadAllCoverPoses(posesFolder);
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(ToggleDebugKey.Value))
            {
                ShowDebugOverlay.Value = !ShowDebugOverlay.Value;
                Log.LogInfo($"[{PluginName}] Debug overlay toggled: {ShowDebugOverlay.Value}");
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
