using HarmonyLib;
using UnityEngine;

namespace CompanionEmotionsMod.Patches
{
    [HarmonyPatch(typeof(UIFreePose), nameof(UIFreePose.Close))]
    public static class UIFreePoseClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Plugin.EnableCastleLifeFix != null && !Plugin.EnableCastleLifeFix.Value) return;

            if (Player.code != null && Player.code.anim != null && Player.code.anim.enabled)
            {
                Player.code.anim.speed = 1f;
            }

            if (Global.code != null && Global.code.companions != null)
            {
                foreach (Transform comp in Global.code.companions.items)
                {
                    if (!comp) continue;

                    Animator anim = comp.GetComponent<Animator>();
                    // Если anim.enabled == false, персонаж осознанно заморожен через Keep Pose в Advanced Free Pose
                    if (anim != null && anim.enabled)
                    {
                        anim.speed = 1f;
                    }
                }
            }
        }
    }
}
