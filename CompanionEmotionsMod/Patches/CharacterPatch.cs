using HarmonyLib;
using UnityEngine;

namespace CompanionEmotionsMod.Patches
{
    [HarmonyPatch(typeof(CharacterCustomization), "Start")]
    public static class CharacterPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CharacterCustomization __instance)
        {
            if (__instance.isDisplay || (__instance.transform.parent != null && __instance.transform.parent.name.Contains("Preset")))
            {
                return;
            }

            var controller = __instance.gameObject.GetComponent<CompanionEmotionController>();
            if (controller == null)
            {
                controller = __instance.gameObject.AddComponent<CompanionEmotionController>();
                Plugin.Log.LogInfo($"[CompanionEmotions] Successfully attached CompanionEmotionController to: {__instance.gameObject.name}");
            }
        }
    }
}
