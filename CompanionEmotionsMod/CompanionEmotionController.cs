using System;
using System.Collections.Generic;
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

        // Индексы всех материалов кожи из CharacterCustomization.RefreshAppearence()
        private static readonly int[] SkinMaterialIndices = { 0, 1, 2, 4, 5, 8, 16, 17, 18, 19, 20 };

        // Параметры процедурного дыхания через блендшейпы
        private float _breathPhase;
        private float _baseStomachWeight;
        private float _baseChestWeight;
        private bool _breathBaseCaptured;

        // Вспомогательное свойство внутри CompanionEmotionController:
        private bool IsDisabledForPlayer => _customization != null && _customization._Player != null && (Plugin.IncludePlayerCharacter == null || !Plugin.IncludePlayerCharacter.Value);

        // Advanced lingerie state caching
        private bool _braHidesPanties;

        // Кэш трансформов костей скелета и выбранный вариант позы ENF
        private readonly Dictionary<string, Transform> _boneTransforms = new Dictionary<string, Transform>();
        private CoverPoseData _assignedCoverPose;
        private float _coverBlendWeight;
        private float _standingPoseWeight;
        private float _stopDelayTimer;
        private UnityEngine.AI.NavMeshAgent _navAgent;
        private Rigidbody _rigidbody;
        private Vector3 _lastPosition;


        private void Awake()
        {
            _customization = GetComponent<CharacterCustomization>();
        }

        private void Start()
        {
            if (_customization == null)
            {
                _customization = GetComponent<CharacterCustomization>();
            }
            if (_customization == null)
            {
                Destroy(this);
                return;
            }

            _audioSource = _customization.aud;
            _animator = _customization.anim;
            _rigidbody = GetComponent<Rigidbody>();
            _lastPosition = transform.position;

            CacheBones();
            CacheBlendShape();

            _navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            CacheAllPoseBones();
            AssignCoverPoseVariant();

            string charKey = GetCharacterKey();
            if (!string.IsNullOrEmpty(charKey) && Plugin.EmotionMemory.TryGetValue(charKey, out var saved))
            {
                Embarrassment = saved.Embarrassment;
                Arousal = saved.Arousal;
            }

            if (_animator != null)
            {
                for (int i = 0; i < _animator.layerCount; i++)
                {
                    Plugin.Log.LogInfo($"[CompanionEmotions] {gameObject.name} Animator Layer {i}: {_animator.GetLayerName(i)} (Weight: {_animator.GetLayerWeight(i)})");
                }
            }
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
            if (IsDisabledForPlayer) return;
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

            // Проверяем, можно ли применить позу прикрывания (не сидит на мебели + раздета + выше порога)
            bool isInFreePose = _animator != null && !_animator.enabled;
            bool canPose = _customization.interactingObject == null && !isInFreePose;
            bool shouldCover = canPose && (_isTopNaked || _isBottomNaked) && Embarrassment >= CoverThreshold;
            _isCoveringSelf = shouldCover;

            // Плавный переход веса позы за 0.5 секунды (скорость 2.0/сек)
            float targetWeight = _isCoveringSelf ? 1.0f : 0.0f;
            _coverBlendWeight = Mathf.MoveTowards(_coverBlendWeight, targetWeight, 2.0f * dt);

            string charKey = GetCharacterKey();
            if (!string.IsNullOrEmpty(charKey))
            {
                Plugin.EmotionMemory[charKey] = new Plugin.SavedEmotionState
                {
                    Embarrassment = this.Embarrassment,
                    Arousal = this.Arousal
                };
            }
        }

        private void LateUpdate()
        {
            if (IsDisabledForPlayer) return;
            // Apply blendshape weight in LateUpdate to override vanilla face animator updates
            if (_customization.body != null && _ashamedBlendShapeIndex >= 0)
            {
                float weight = Mathf.Clamp(Embarrassment * 0.8f, 0f, 80f);
                _customization.body.SetBlendShapeWeight(_ashamedBlendShapeIndex, weight);
            }

            // 2. Плавно обновляем декаль румянца на лице, губах и ушах
            UpdateFaceBlush();

            // 3. Обновляем блеск кожи и частоту дыхания
            UpdatePhysiology();

            // 4. Обновляем позу ENF
            UpdateCoverPose();
        }

        private void UpdateFaceBlush()
        {
            if (_customization == null || _customization.body == null) return;

            Material[] mats = _customization.body.materials;
            if (mats == null || mats.Length <= 4) return;

            // Если лицевая татуировка не надета и загружена наша face_blush.png — ставим её в слот
            if (_customization.faceTatoos == null && Plugin.BlushTexture != null)
            {
                if (mats[1].GetTexture("_MakeUpMask2_RGB") != Plugin.BlushTexture)
                {
                    mats[1].SetTexture("_MakeUpMask2_RGB", Plugin.BlushTexture);
                    mats[2].SetTexture("_MakeUpMask2_RGB", Plugin.BlushTexture);
                    mats[4].SetTexture("_MakeUpMask2_RGB", Plugin.BlushTexture);
                }
            }

            // Плавно меняем прозрачность цвета (от 0 до MaxBlushAlpha) в зависимости от Embarrassment
            float normalizedEmb = Mathf.Clamp01(Embarrassment / 100f);
            Color targetColor = Plugin.BlushColorConfig != null ? Plugin.BlushColorConfig.Value : new Color(0.85f, 0.22f, 0.28f, 1f);
            targetColor.a = normalizedEmb * (Plugin.MaxBlushAlpha != null ? Plugin.MaxBlushAlpha.Value : 0.75f);

            mats[1].SetColor("_Mask2_Rchannel_ColorAmountA", targetColor);
            mats[2].SetColor("_Mask2_Rchannel_ColorAmountA", targetColor);
            mats[4].SetColor("_Mask2_Rchannel_ColorAmountA", targetColor);
        }

        private void UpdatePhysiology()
        {
            if (_customization == null || _customization.body == null) return;

            // Общий индекс волнения от 0.0 до 1.0 (максимум из смущения и возбуждения)
            float stressFactor = Mathf.Clamp01(Mathf.Max(Embarrassment, Arousal) / 100f);

            // 1. Влажный блеск кожи (_SmoothnessDeviate) с гарантированным порогом для сухой кожи
            Material[] mats = _customization.body.materials;
            if (mats != null && mats.Length > 20)
            {
                float baseGloss = _customization.skinGlossiness;
                float boost = Plugin.SweatGlossBoost != null ? Plugin.SweatGlossBoost.Value : 0.30f;
                float minPeak = Plugin.MinPeakSweatGloss != null ? Plugin.MinPeakSweatGloss.Value : 0.12f;

                float peakGloss = Mathf.Max(baseGloss + boost, minPeak);
                float targetSmoothness = Mathf.Lerp(baseGloss, peakGloss, stressFactor);

                for (int i = 0; i < SkinMaterialIndices.Length; i++)
                {
                    int idx = SkinMaterialIndices[i];
                    if (mats[idx] != null)
                    {
                        mats[idx].SetFloat("_SmoothnessDeviate", targetSmoothness);
                    }
                }
            }

            // 2. Непрерывное дыхание через блендшейпы грудной клетки и живота
            if (Player.code != null)
            {
                int stomachIdx = Player.code.stomachDepthIndex;
                int chestIdx = Player.code.chestWidthIndex;

                // Запоминаем базовый вес блендшейпа из пресета персонажа при первом кадре
                if (!_breathBaseCaptured && stomachIdx >= 0 && chestIdx >= 0)
                {
                    _baseStomachWeight = _customization.body.GetBlendShapeWeight(stomachIdx);
                    _baseChestWeight = _customization.body.GetBlendShapeWeight(chestIdx);
                    _breathBaseCaptured = true;
                }

                if (_breathBaseCaptured)
                {
                    // Частота плавно растёт от 1.8 рад/с (покой) до 4.8 рад/с (пик волнения)
                    float freq = Mathf.Lerp(1.8f, 4.8f, stressFactor);
                    _breathPhase += Time.deltaTime * freq;

                    // Синусоида вдоха (0..1) и глубина вдоха (от 3 до 12 единиц)
                    float wave = (Mathf.Sin(_breathPhase) + 1f) * 0.5f;
                    float amp = Mathf.Lerp(3f, 12f, stressFactor);

                    _customization.body.SetBlendShapeWeight(stomachIdx, _baseStomachWeight + wave * amp);
                    _customization.body.SetBlendShapeWeight(chestIdx, _baseChestWeight + wave * (amp * 0.8f));
                }
            }
        }

        private void UpdateCoverPose()
        {
            if (_coverBlendWeight <= 0.001f || _assignedCoverPose == null)
            {
                _lastPosition = transform.position;
                _standingPoseWeight = 0f;
                _stopDelayTimer = 0f;
                return;
            }

            float dt = Time.deltaTime;
            float movedDistSq = (transform.position - _lastPosition).sqrMagnitude;
            _lastPosition = transform.position;

            bool isMoving = movedDistSq > 0.00005f;
            if (!isMoving && _navAgent != null && _navAgent.enabled)
            {
                isMoving = _navAgent.velocity.sqrMagnitude > 0.01f;
            }
            if (!isMoving && _customization != null && _customization._Player != null)
            {
                isMoving = PMC_Input.forwardHold || PMC_Input.backHold || PMC_Input.leftHold || PMC_Input.rightHold;
            }

            // Если персонаж движется — взводим таймер ожидания 0.3 сек после остановки
            if (isMoving)
            {
                _stopDelayTimer = 0.3f;
            }
            else if (_stopDelayTimer > 0f)
            {
                _stopDelayTimer = Mathf.MoveTowards(_stopDelayTimer, 0f, dt);
            }

            // Ноги и корпус возвращаются в позу только когда движение прекратилось И прошло 0.5 сек
            bool canApplyStandingPose = !isMoving && _stopDelayTimer <= 0.001f;
            float targetStandingWeight = canApplyStandingPose ? _coverBlendWeight : 0f;
            float standBlendSpeed = isMoving ? 8.0f : 2.5f; // Сбрасываем мгновенно при старте бега, возвращаем плавно

            _standingPoseWeight = Mathf.MoveTowards(_standingPoseWeight, targetStandingWeight, standBlendSpeed * dt);

            // Опускаем таз (hip) по высоте из позы, чтобы ступни стояли на полу без левитации
            if (_standingPoseWeight > 0.001f && _assignedCoverPose.HasHipPosition &&
                _boneTransforms.TryGetValue("hip", out Transform hipTransform) && hipTransform != null)
            {
                hipTransform.localPosition = Vector3.Lerp(
                    hipTransform.localPosition,
                    _assignedCoverPose.HipLocalPosition,
                    _standingPoseWeight
                );
            }

            foreach (var kvp in _assignedCoverPose.BoneRotations)
            {
                string boneName = kvp.Key;
                bool isArmBone = PoseLoader.ArmBones.Contains(boneName);

                float weight = isArmBone ? _coverBlendWeight : _standingPoseWeight;
                if (weight <= 0.001f) continue;

                if (_boneTransforms.TryGetValue(boneName, out Transform boneTransform) && boneTransform != null)
                {
                    boneTransform.localRotation = Quaternion.Slerp(
                        boneTransform.localRotation,
                        kvp.Value,
                        weight
                    );
                }
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
                    _isLookedAtByCamera = camDot > 0.97f;

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

        private string GetCharacterKey()
        {
            if (_customization == null) return null;
            if (!string.IsNullOrEmpty(_customization.characterName))
                return _customization.characterName;
            return gameObject.name;
        }

        private void CacheAllPoseBones()
        {
            _boneTransforms.Clear();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (PoseLoader.UpperBodyBones.Contains(t.name) || PoseLoader.LowerBodyBones.Contains(t.name))
                {
                    if (!_boneTransforms.ContainsKey(t.name))
                    {
                        _boneTransforms[t.name] = t;
                    }
                }
            }
        }

        private void AssignCoverPoseVariant()
        {
            if (PoseLoader.CoverPoseVariants.Count == 0) return;

            string key = GetCharacterKey() ?? gameObject.name;
            int index = Mathf.Abs(key.GetHashCode()) % PoseLoader.CoverPoseVariants.Count;
            _assignedCoverPose = PoseLoader.CoverPoseVariants[index];
        }

        public void AddArousal(float amount) => Arousal = Mathf.Clamp(Arousal + amount, 0f, 100f);
        public void AddEmbarrassmentImpulse(float amount) => Embarrassment = Mathf.Clamp(Embarrassment + amount, 0f, 100f);

        private void OnGUI()
        {
            if (!Plugin.ShowDebugOverlay.Value || IsDisabledForPlayer) return;

            Camera cam = Camera.main;
            if (cam == null || _headBone == null) return;
            if (Vector3.Distance(cam.transform.position, _headBone.position) > 5f) return;

            Vector3 screenPos = cam.WorldToScreenPoint(_headBone.position + Vector3.up * 0.3f);
            if (screenPos.z <= 0) return;

            float y = Screen.height - screenPos.y;

            string armorStr = _customization.armor != null ? $"Act:{_customization.armor.gameObject.activeInHierarchy}" : "Null";
            string braStr = _customization.bra != null ? $"Act:{_customization.bra.gameObject.activeInHierarchy} (HidePanty:{_braHidesPanties})" : "Null";
            string pantyStr = _customization.panties != null ? $"Act:{_customization.panties.gameObject.activeInHierarchy}" : "Null";

            string text = $"{gameObject.name}\n" +
                          $"Emb: {Embarrassment:F1} (Target: {TargetEmbarrassment:F0}) | Arousal: {Arousal:F1}\n" +
                          $"Top -> Armor: {armorStr} | Bra: {braStr} => [TopNaked: {_isTopNaked}]\n" +
                          $"Bot -> Armor: {armorStr} | Panty: {pantyStr} => [BotNaked: {_isBottomNaked}]\n" +
                          $"CamLook: {_isLookedAtByCamera} | SeesCam: {_canSeeCamera} | Covering: {_isCoveringSelf}";

            GUI.Box(new Rect(screenPos.x - 280, y - 65, 560, 100), text);
        }
    }
}
