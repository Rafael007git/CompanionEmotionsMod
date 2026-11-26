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
    }

    public static class LoveInteractionInterpreter
    {
        private class ActiveContactState
        {
            public PhysicalInteractionType Type;
            public bool IsEngaged;
            public float EngagedDuration;
            public float LastApproachSpeed;
            public float PulseCooldown;
        }

        private static readonly Dictionary<string, InteractionRule> _rules = new Dictionary<string, InteractionRule>();
        private static readonly Dictionary<string, ActiveContactState> _activeContacts = new Dictionary<string, ActiveContactState>();

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
                EnterRadius = 0.14f,
                ExitRadius = 0.18f,
                Priority = 100,
                ArousalGainPerSec = 4.0f,
                EmbarrassmentGainPerSec = 2.0f
            });

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.OralToVagina,
                SourceZone = "Mouth",
                TargetZone = "Vagina",
                EnterRadius = 0.15f,
                ExitRadius = 0.20f,
                Priority = 95,
                ArousalGainPerSec = 9.0f,
                EmbarrassmentGainPerSec = 6.0f
            });

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.HandToBreast,
                SourceZone = "Hands",
                TargetZone = "Pectorals",
                EnterRadius = 0.16f,
                ExitRadius = 0.21f,
                Priority = 70,
                ArousalGainPerSec = 5.5f,
                EmbarrassmentGainPerSec = 4.0f
            });

            AddRule(new InteractionRule
            {
                Type = PhysicalInteractionType.HandToThigh,
                SourceZone = "Hands",
                TargetZone = "Thighs",
                EnterRadius = 0.15f,
                ExitRadius = 0.20f,
                Priority = 60,
                ArousalGainPerSec = 3.5f,
                EmbarrassmentGainPerSec = 3.0f
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
            float approachSpeed)
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
                Plugin.Log?.LogInfo($"[LoveInterpreter] START {rule.Type}: {source.characterName} -> {target.characterName} (dist={distance:F3}m)");
            }
            else if (state.IsEngaged && distance > rule.ExitRadius)
            {
                state.IsEngaged = false;
                Plugin.Log?.LogInfo($"[LoveInterpreter] END {rule.Type}: {source.characterName} -> {target.characterName} (duration={state.EngagedDuration:F1}s)");
                return;
            }

            if (!state.IsEngaged) return;

            const float dt = 0.05f;
            state.EngagedDuration += dt;
            if (state.PulseCooldown > 0f) state.PulseCooldown -= dt;

            if (state.LastApproachSpeed > 0.12f && approachSpeed <= 0.02f && state.PulseCooldown <= 0f)
            {
                state.PulseCooldown = 0.25f;
                Plugin.Log?.LogInfo($"[LoveInterpreter] PULSE {rule.Type}: {source.characterName} -> {target.characterName} (speed={state.LastApproachSpeed:F2}m/s)");
            }
            state.LastApproachSpeed = approachSpeed;

            ApplyEmotionalDelta(target, rule.ArousalGainPerSec * dt, rule.EmbarrassmentGainPerSec * dt);
        }

        private static void ApplyEmotionalDelta(CharacterCustomization target, float dArousal, float dEmbarrassment)
        {
            if (target == null || string.IsNullOrEmpty(target.characterName)) return;

            Plugin.EmotionMemory.TryGetValue(target.characterName, out var mem);
            mem.Arousal = Mathf.Clamp(mem.Arousal + dArousal, 0f, 100f);
            mem.Embarrassment = Mathf.Clamp(mem.Embarrassment + dEmbarrassment, 0f, 100f);
            Plugin.EmotionMemory[target.characterName] = mem;
        }
    }
}
