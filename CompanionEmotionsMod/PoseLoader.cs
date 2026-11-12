using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CompanionEmotionsMod
{
    public static class PoseLoader
    {
        // Список всех загруженных вариантов позы ENF (enf_cover-01.bytes, enf_cover-02.bytes и т.д.)
        public static readonly List<Dictionary<string, Quaternion>> CoverPoseVariants = new List<Dictionary<string, Quaternion>>();

        // Наборы костей для разделения верха тела (работает даже на ходу) и ног (только стоя на месте)
        public static readonly HashSet<string> UpperBodyBones = new HashSet<string>
        {
            "hip", "abdomenLower", "abdomenUpper", "chestLower", "chestUpper",
            "neckLower", "neckUpper", "head",
            "lCollar", "lShldrBend", "lShldrTwist", "lForearmBend", "lForearmTwist", "lHand",
            "lThumb1", "lThumb2", "lThumb3", "lIndex1", "lIndex2", "lIndex3",
            "lMid1", "lMid2", "lMid3", "lRing1", "lRing2", "lRing3", "lPinky1", "lPinky2", "lPinky3",
            "rCollar", "rShldrBend", "rShldrTwist", "rForearmBend", "rForearmTwist", "rHand",
            "rThumb1", "rThumb2", "rThumb3", "rIndex1", "rIndex2", "rIndex3",
            "rMid1", "rMid2", "rMid3", "rRing1", "rRing2", "rRing3", "rPinky1", "rPinky2", "rPinky3"
        };

        public static readonly HashSet<string> LowerBodyBones = new HashSet<string>
        {
            "pelvis",
            "lThighBend", "lThighTwist", "lShin", "lFoot",
            "rThighBend", "rThighTwist", "rShin", "rFoot"
        };

        public static void LoadAllCoverPoses(string posesFolder)
        {
            CoverPoseVariants.Clear();

            if (!Directory.Exists(posesFolder))
            {
                Directory.CreateDirectory(posesFolder);
            }

            string[] files = Directory.GetFiles(posesFolder, "enf_cover-*.bytes");
            System.Array.Sort(files);

            foreach (string filePath in files)
            {
                var boneMap = new Dictionary<string, Quaternion>();

                foreach (string bone in UpperBodyBones)
                {
                    string tagPath = filePath + "?tag=rotation" + bone;
                    if (global::ES2.Exists(tagPath))
                    {
                        boneMap[bone] = global::ES2.Load<Quaternion>(tagPath);
                    }
                }

                foreach (string bone in LowerBodyBones)
                {
                    string tagPath = filePath + "?tag=rotation" + bone;
                    if (global::ES2.Exists(tagPath))
                    {
                        boneMap[bone] = global::ES2.Load<Quaternion>(tagPath);
                    }
                }

                if (boneMap.Count > 0)
                {
                    CoverPoseVariants.Add(boneMap);
                    Plugin.Log.LogInfo($"[CompanionEmotions] Loaded ENF cover pose '{Path.GetFileName(filePath)}' ({boneMap.Count} bones).");
                }
            }

            if (CoverPoseVariants.Count == 0)
            {
                Plugin.Log.LogWarning($"[CompanionEmotions] No enf_cover-*.bytes files found in: {posesFolder}");
            }
        }
    }
}
