using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
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
        public const string PluginVersion = "0.1.3";

        public static ManualLogSource Log;
        private Harmony harmony;

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

        // Loaded custom blush texture
        public static Texture2D BlushTexture;

        public struct SavedEmotionState
        {
            public float Embarrassment;
            public float Arousal;
        }

        public static readonly System.Collections.Generic.Dictionary<string, SavedEmotionState> EmotionMemory = new System.Collections.Generic.Dictionary<string, SavedEmotionState>();

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"[{PluginName}] Initializing v{PluginVersion}...");

            try
            {
                InitConfig();
                LoadBlushTexture();

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
                false,
                "True: enable emotional reactions and debug overlay for the main player character. False: companions only."
            );

            ShowDebugOverlay = Config.Bind(
                "Debug Settings",
                "Show Debug Overlay",
                true,
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
                15f,
                "Base speed of embarrassment increase per second."
            );

            BaseDecaySpeed = Config.Bind(
                "Balance Settings",
                "Base Decay Speed",
                10f,
                "Base speed of embarrassment decrease per second."
            );

            BlushColorConfig = Config.Bind(
                "Visual Settings",
                "Blush Color",
                new Color(0.85f, 0.22f, 0.28f, 1f),
                "Tint color for the dynamic cheek blush decal."
            );

            MaxBlushAlpha = Config.Bind(
                "Visual Settings",
                "Max Blush Intensity",
                0.75f,
                "Maximum alpha transparency (0.0 to 1.0) of the blush decal at 100 Embarrassment."
            );

            SweatGlossBoost = Config.Bind(
                "Visual Settings",
                "Sweat Gloss Boost",
                0.30f,
                "Additional skin smoothness added at 100% emotional stress/arousal."
            );

            MinPeakSweatGloss = Config.Bind(
                "Visual Settings",
                "Min Peak Sweat Gloss",
                0.12f,
                "Minimum target skin smoothness at 100% stress (ensures visible sweat sheen even on dry skin presets)."
            );
        }

        private void LoadBlushTexture()
        {
            // Стандартный путь SWPT: BepInEx\plugins\CompanionEmotionsMod\textures\face_blush.png
            string modFolder = Path.Combine(Paths.PluginPath, "CompanionEmotionsMod");
            string texturesFolder = Path.Combine(modFolder, "textures");

            // Автоматически создаем структуру папок, если её ещё нет
            if (!Directory.Exists(texturesFolder))
            {
                Directory.CreateDirectory(texturesFolder);
            }

            string targetPath = Path.Combine(texturesFolder, "face_blush.png");

            if (File.Exists(targetPath))
            {
                BlushTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                BlushTexture.LoadImage(File.ReadAllBytes(targetPath));
                Log.LogInfo($"[{PluginName}] Successfully loaded face_blush.png from: {targetPath}");
            }
            else
            {
                Log.LogWarning($"[{PluginName}] face_blush.png not found! Please place the texture at: {targetPath}");
            }
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
