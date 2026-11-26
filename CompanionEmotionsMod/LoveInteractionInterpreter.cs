using FurnitureAnimations;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CompanionEmotionsMod
{
    public enum PhysicalInteractionType
    {
        None = 0,
        MutualKiss,       // Mouth <-> Mouth
        OralToVagina,     // Mouth -> Vagina / Clitoris
        OralToBreast,     // Mouth -> Pectorals
        HandToBreast,     // Hands -> Pectorals
        HandToGroin,      // Hands -> Vagina / Clitoris / Pelvis
        HandToThigh,      // Hands -> Thighs
        PelvicContact,    // Clitoris / Pelvis <-> Clitoris / Pelvis
        FootStimulation   // Feet -> Vagina / Pelvis / Pectorals
    }

    public class InteractionRule
    {
        public PhysicalInteractionType Type;
        public string SourceZone;
        public string TargetZone;
        public float EnterRadius;
        public float ExitRadius;
        public int Priority;
        public float ArousalGainPerSec;
        public float EmbarrassmentGainPerSec;
        public float MotionArousalMultiplier = 3.0f;
        public float MotionEmbarrassmentMultiplier = 1.2f;
    }

    public static class LoveInteractionInterpreter
    {
        private class ActiveContactState
        {
            public PhysicalInteractionType Type;
            public bool IsEngaged;
            public float EngagedDuration;
            public bool WasMoving;
        }

        private static readonly Dictionary<string, InteractionRule> _rules = new Dictionary<string, InteractionRule>();
        private static readonly Dictionary<string, ActiveContactState> _activeContacts = new Dictionary<string, ActiveContactState>();

        private const float CaressSpeedThreshold = 0.035f; // 3.5 см/сек — порог нежного скольжения/движения

        public static void Initialize()
        {
            RegisterDefaultRules();
            LoveKinematicBridge.OnProximitySample -= HandleProximitySample;
            LoveKinematicBridge.OnProximitySample += HandleProximitySample;
            Plugin.Log?.LogInfo("[LoveInterpreter] Subscribed to LoveKinematicBridge.");
        }

        private static void RegisterDefaultRules()
        {
            _rules.Clear();

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.MutualKiss,
                SourceZone = "Mouth",
                TargetZone = "Mouth",
                EnterRadius = 0.16f,
                ExitRadius = 0.21f,
                Priority = 100,
                ArousalGainPerSec = 2.0f,
                EmbarrassmentGainPerSec = 1.5f,
                MotionArousalMultiplier = 2.5f,
                MotionEmbarrassmentMultiplier = 1.1f
            });

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.OralToVagina,
                SourceZone = "Mouth",
                TargetZone = "Vagina",
                EnterRadius = 0.18f,
                ExitRadius = 0.23f,
                Priority = 95,
                ArousalGainPerSec = 4.0f,
                EmbarrassmentGainPerSec = 4.0f,
                MotionArousalMultiplier = 3.0f,
                MotionEmbarrassmentMultiplier = 1.2f
            });

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.HandToBreast,
                SourceZone = "Hands",
                TargetZone = "Pectorals",
                EnterRadius = 0.25f,
                ExitRadius = 0.30f,
                Priority = 70,
                ArousalGainPerSec = 2.0f,
                EmbarrassmentGainPerSec = 2.5f,
                MotionArousalMultiplier = 3.0f,
                MotionEmbarrassmentMultiplier = 1.2f
            });

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.HandToThigh,
                SourceZone = "Hands",
                TargetZone = "Thighs",
                EnterRadius = 0.20f,
                ExitRadius = 0.25f,
                Priority = 60,
                ArousalGainPerSec = 1.5f,
                EmbarrassmentGainPerSec = 2.0f,
                MotionArousalMultiplier = 2.5f,
                MotionEmbarrassmentMultiplier = 1.1f
            });

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.PelvicContact,
                SourceZone = "Clitoris",
                TargetZone = "Vagina",
                EnterRadius = 0.16f,
                ExitRadius = 0.21f,
                Priority = 90,
                ArousalGainPerSec = 5.0f,
                EmbarrassmentGainPerSec = 4.0f,
                MotionArousalMultiplier = 3.0f,
                MotionEmbarrassmentMultiplier = 1.2f
            });
        }

        private static void AddRule(InteractionRule rule)
        {
            _rules[$"{rule.SourceZone}->{rule.TargetZone}"] = rule;
        }

        private static void HandleProximitySample(
            CharacterCustomization source,
            string sourceZone,
            CharacterCustomization target,
            string targetZone,
            float distance,
            float relativeSpeed)
        {
            if (source == null || target == null) return;

            string ruleKey = $"{sourceZone}->{targetZone}";
            if (!_rules.TryGetValue(ruleKey, out var rule)) return;

            string pairKey = $"{source.GetInstanceID()}_{target.GetInstanceID()}_{rule.Type}";
            if (!_activeContacts.TryGetValue(pairKey, out var state))
            {
                state = new ActiveContactState { Type = rule.Type };
                _activeContacts[pairKey] = state;
            }

            if (!state.IsEngaged && distance <= rule.EnterRadius)
            {
                state.IsEngaged = true;
                state.EngagedDuration = 0f;
                state.WasMoving = false;
                Plugin.Log?.LogInfo($"[LoveInterpreter] START {rule.Type}: {source.characterName} -> {target.characterName} (dist={distance:F3}m)");
            }
            else if (state.IsEngaged && distance > rule.ExitRadius)
            {
                state.IsEngaged = false;
                state.WasMoving = false;
                Plugin.Log?.LogInfo($"[LoveInterpreter] END {rule.Type}: {source.characterName} -> {target.characterName} (duration={state.EngagedDuration:F1}s)");
                return;
            }

            if (!state.IsEngaged) return;

            const float dt = 0.05f;
            state.EngagedDuration += dt;

            bool isMoving = relativeSpeed > CaressSpeedThreshold;
            if (isMoving && !state.WasMoving)
            {
                Plugin.Log?.LogInfo($"[LoveInterpreter] CARESS/MOTION {rule.Type}: {source.characterName} -> {target.characterName} (speed={relativeSpeed:F2}m/s)");
            }
            state.WasMoving = isMoving;

            float arousalMult = isMoving ? rule.MotionArousalMultiplier : 1.0f;
            float embMult = isMoving ? rule.MotionEmbarrassmentMultiplier : 1.0f;

            ApplyEmotionalDelta(
                target,
                rule.ArousalGainPerSec * arousalMult * dt,
                rule.EmbarrassmentGainPerSec * embMult * dt
            );
        }
        private static void ApplyEmotionalDelta(CharacterCustomization target, float dArousal, float dEmbarrassment)
        {
            if (target == null) return;

            var controller = target.GetComponent<CompanionEmotionController>();
            if (controller != null)
            {
                controller.AddArousal(dArousal);
                controller.AddEmbarrassmentImpulse(dEmbarrassment);
            }
        }
    }
}