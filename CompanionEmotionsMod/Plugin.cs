using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace CompanionEmotionsMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.lorifel007.furnitureanimations", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("aedenthorn.CustomTattooPack", BepInDependency.DependencyFlags.SoftDependency)] // Предполагаемый ID мода тату
    [BepInProcess("She Will Punish Them.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lorifel007.companionemotions";
        public const string PluginName = "Companion Emotions Mod";
        public const string PluginVersion = "0.1.0";

        public static ManualLogSource Log;
        private Harmony harmony;

        // Настройки конфигурации
        public static ConfigEntry<bool> ShowDebugOverlay;
        public static ConfigEntry<KeyCode> ToggleDebugKey;
        public static ConfigEntry<float> BaseGainSpeed;
        public static ConfigEntry<float> BaseDecaySpeed;
        public static ConfigEntry<bool> EnableLiveLLM;
        public static ConfigEntry<string> LlmApiUrl;

        private void Awake()
        {
            Log = Logger;
            Log.LogWarning($"[{PluginName}] Старт инициализации плагина v{PluginVersion}...");

            try
            {
                // Привязка настроек конфигурации
                InitConfig();

                // Применяем Harmony патчи
                harmony = new Harmony(PluginGuid);
                harmony.PatchAll();

                Log.LogWarning($"[{PluginName}] Плагин успешно загружен и применил патчи!");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[{PluginName}] Сбой при загрузке плагина: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void InitConfig()
        {
            ShowDebugOverlay = Config.Bind(
                "Debug Settings",
                "Show Debug Overlay",
                true,
                "Показывать зеленое диагностическое окно смущения и эмоций над головой спутницы."
            );

            ToggleDebugKey = Config.Bind(
                "Debug Settings",
                "Toggle Overlay Key",
                KeyCode.F8,
                "Клавиша для быстрого включения/выключения оверлея в игре."
            );

            BaseGainSpeed = Config.Bind(
                "Balance Settings",
                "Base Gain Speed",
                15f,
                "Базовая скорость роста шкалы смущения (единиц в секунду) при активных раздражителях."
            );

            BaseDecaySpeed = Config.Bind(
                "Balance Settings",
                "Base Decay Speed",
                10f,
                "Базовая скорость спада шкалы смущения (единиц в секунду) в спокойном состоянии."
            );

            EnableLiveLLM = Config.Bind(
                "LLM Settings",
                "Enable Live LLM AI",
                false,
                "True: включать динамическую генерацию реплик через локальную нейросеть. False: использовать готовый JSON-словарь фраз."
            );

            LlmApiUrl = Config.Bind(
                "LLM Settings",
                "LLM API Endpoint",
                "http://localhost:11434/v1/chat/completions",
                "URL локального сервера ИИ (Ollama, LM Studio и др.) для генерации живых реплик."
            );
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleDebugKey.Value))
            {
                ShowDebugOverlay.Value = !ShowDebugOverlay.Value;
                Log.LogInfo($"[{PluginName}] Отображение оверлея переключено в: {ShowDebugOverlay.Value}");
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
