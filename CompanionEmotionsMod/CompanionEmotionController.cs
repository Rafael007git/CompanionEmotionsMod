using System;
using UnityEngine;

namespace CompanionEmotionsMod
{
    public class CompanionEmotionController : MonoBehaviour
    {
        // Main emotion scales (0..100)
        public float Embarrassment { get; private set; }
        public float TargetEmbarrassment { get; private set; }
        public float Arousal { get; private set; }

        // Threshold for the hands-covering idle pose
        public float CoverThreshold => 55f;

        // References to vanilla game components
        private CharacterCustomization _customization;
        private AudioSource _audioSource;
        private Animator _animator;

        // Key skeletal bones for look calculation
        private Transform _centerTargetBone; // chestLower or hip
        private Transform _headBone;

        // Facemorph blendshape index
        private int _ashamedBlendShapeIndex = -1;
        private float _sensorTimer;

        // Diagnostic states
        private bool _isTopNaked;
        private bool _isBottomNaked;
        private bool _isLookedAtByCamera;
        private bool _canSeeCamera;
        private bool _isCoveringSelf;

        // Advanced lingerie state caching
        private bool _braHidesPanties;

        private void Start()
        {
            _customization = GetComponent<CharacterCustomization>();
            if (_customization == null)
            {
                Destroy(this);
                return;
            }

            _audioSource = _customization.aud;
            _animator = _customization.anim;

            CacheBones();
            CacheBlendShape();
        }

        private void CacheBones()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "chestLower")
                    _centerTargetBone = t;
                else if (t.name == "hip" && _centerTargetBone == null)
                    _centerTargetBone = t;
                else if (t.name == "head")
                    _headBone = t;
            }
        }

        private void CacheBlendShape()
        {
            if (_customization.body != null && _customization.body.sharedMesh != null)
            {
                _ashamedBlendShapeIndex = _customization.body.sharedMesh.GetBlendShapeIndex("head__eCTRL_i3DMHAshamed");
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Poll sensors 4 times per second to save CPU
            _sensorTimer += dt;
            if (_sensorTimer >= 0.25f)
            {
                _sensorTimer = 0f;
                EvaluateSensors();
            }

            // Universal dampener: high Arousal suppresses Embarrassment growth (up to -75% at peak)
            float arousalDampener = Mathf.Lerp(1.0f, 0.25f, Arousal / 100f);
            float effectiveTargetEmb = TargetEmbarrassment * arousalDampener;

            float embSpeed = (effectiveTargetEmb > Embarrassment)
                ? Plugin.BaseGainSpeed.Value * arousalDampener
                : Plugin.BaseDecaySpeed.Value;

            Embarrassment = Mathf.MoveTowards(Embarrassment, effectiveTargetEmb, embSpeed * dt);

            // Natural decay for Arousal (placeholder for Phase 2)
            Arousal = Mathf.MoveTowards(Arousal, 0f, 4.0f * dt);

            // Idle cover pose state verification
            bool shouldCover = (_isTopNaked || _isBottomNaked) && Embarrassment >= CoverThreshold;
            if (shouldCover != _isCoveringSelf)
            {
                _isCoveringSelf = shouldCover;
            }
        }

        private void LateUpdate()
        {
            // Apply blendshape weight in LateUpdate to override vanilla face animator updates
            if (_customization.body != null && _ashamedBlendShapeIndex >= 0)
            {
                float weight = Mathf.Clamp(Embarrassment * 0.8f, 0f, 80f);
                _customization.body.SetBlendShapeWeight(_ashamedBlendShapeIndex, weight);
            }
        }

        private void EvaluateSensors()
        {
            // --- VANILLA CLOTHES INITIAL SCAN ---
            bool armorActive = _customization.armor != null && _customization.armor.gameObject.activeInHierarchy;
            bool braActive = _customization.bra != null && _customization.bra.gameObject.activeInHierarchy;
            bool pantiesActive = _customization.panties != null && _customization.panties.gameObject.activeInHierarchy;

            // Reset advanced lingerie flag
            _braHidesPanties = false;

            // Dynamic check for full-body lingerie (dresses, bodysuits, closed swimsuits)
            if (braActive)
            {
                try
                {
                    var itemComp = _customization.bra.GetComponent<Item>();
                    if (itemComp != null)
                    {
                        var appealComp = itemComp.GetAppeal();
                        if (appealComp != null)
                        {
                            // Reads the vanilla flag that automatically hides panties mesh
                            _braHidesPanties = appealComp.hidePanties;
                        }
                    }
                }
                catch (Exception)
                {
                    // Soft fallback if component structure temporarily fails during outfit swaps
                    _braHidesPanties = false;
                }
            }

            // --- NUDITY DETERMINATION ---
            if (armorActive)
            {
                // Rule 1: Active Armor completely covers BOTH top and bottom zones
                _isTopNaked = false;
                _isBottomNaked = false;
            }
            else
            {
                // Rule 2: Top is naked if NO active Armor AND NO active Bra
                _isTopNaked = !braActive;

                // Rule 3: Bottom is naked if NO active Armor AND NO active Panties...
                // EXCEPT when active Bra acts as full-body lingerie (hidePanties == true)
                _isBottomNaked = !pantiesActive && !_braHidesPanties;
            }

            bool isNaked = _isTopNaked || _isBottomNaked;

            // --- CAMERA & LOOK SENSORS ---
            _isLookedAtByCamera = false;
            _canSeeCamera = false;

            Camera cam = Camera.main;
            if (cam != null && _centerTargetBone != null)
            {
                Vector3 toBody = _centerTargetBone.position - cam.transform.position;
                float dist = toBody.magnitude;

                if (dist > 0.2f && dist < 6.0f)
                {
                    float camDot = Vector3.Dot(cam.transform.forward, toBody.normalized);
                    _isLookedAtByCamera = camDot > 0.90f;

                    if (_headBone != null)
                    {
                        Vector3 toCam = (cam.transform.position - _headBone.position).normalized;
                        float headDot = Vector3.Dot(_headBone.forward, toCam);
                        _canSeeCamera = headDot > 0.25f;
                    }
                }
            }

            // --- TARGET CALCULATION ---
            float target = 0f;
            if (_isTopNaked) target += 35f;
            if (_isBottomNaked) target += 40f;

            if (isNaked && _isLookedAtByCamera)
            {
                target += _canSeeCamera ? 35f : 5f;
            }

            TargetEmbarrassment = Mathf.Clamp(target, 0f, 100f);
        }

        public void AddArousal(float amount) => Arousal = Mathf.Clamp(Arousal + amount, 0f, 100f);
        public void AddEmbarrassmentImpulse(float amount) => Embarrassment = Mathf.Clamp(Embarrassment + amount, 0f, 100f);

        private void OnGUI()
        {
            if (!Plugin.ShowDebugOverlay.Value) return;

            Camera cam = Camera.main;
            if (cam == null || _headBone == null) return;
            if (Vector3.Distance(cam.transform.position, _headBone.position) > 5f) return;

            Vector3 screenPos = cam.WorldToScreenPoint(_headBone.position + Vector3.up * 0.3f);
            if (screenPos.z <= 0) return;

            float y = Screen.height - screenPos.y;

            string armorStr = _customization.armor != null ? $"Exist(Act:{_customization.armor.gameObject.activeInHierarchy})" : "Null";
            string braStr = _customization.bra != null ? $"Exist(Act:{_customization.bra.gameObject.activeInHierarchy}, HidePanty:{_braHidesPanties})" : "Null";
            string pantyStr = _customization.panties != null ? $"Exist(Act:{_customization.panties.gameObject.activeInHierarchy})" : "Null";

            string text = $"{gameObject.name}\n" +
                          $"Emb: {Embarrassment:F1} (T:{TargetEmbarrassment:F0}) | Ar: {Arousal:F1}\n" +
                          $"TopState -> Armor: {armorStr} | Bra: {braStr} -> [TopNaked: {_isTopNaked}]\n" +
                          $"BotState -> Armor: {armorStr} | Panty: {pantyStr} -> [BotNaked: {_isBottomNaked}]\n" +
                          $"CamLook: {_isLookedAtByCamera} | SeesCam: {_canSeeCamera} | Covering: {_isCoveringSelf}";

            // Window width increased to 425px (+25% scaling) to prevent text clipping
            GUI.Box(new Rect(screenPos.x - 212, y - 65, 425, 105), text);
        }
    }
}
