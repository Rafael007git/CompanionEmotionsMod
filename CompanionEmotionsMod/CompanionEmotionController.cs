using System;
using UnityEngine;

namespace CompanionEmotionsMod
{
    public class CompanionEmotionController : MonoBehaviour
    {
        // Основные шкалы (0..100)
        public float Embarrassment { get; private set; }
        public float TargetEmbarrassment { get; private set; }
        public float Arousal { get; private set; }
        public float TargetArousal { get; private set; }

        // Порог для позы прикрывания руками
        public float CoverThreshold => 55f;

        // Ссылки на компоненты игры
        private CharacterCustomization _customization;
        private AudioSource _audioSource;
        private Animator _animator;

        // Кости для обсчета взгляда
        private Transform _centerTargetBone; // chestLower или hip
        private Transform _headBone;

        // Блендшейп лица
        private int _ashamedBlendShapeIndex = -1;
        private float _sensorTimer;

        // Состояния для диагностики
        private bool _isTopNaked;
        private bool _isBottomNaked;
        private bool _isLookedAtByCamera;
        private bool _canSeeCamera;
        private bool _isCoveringSelf;

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
            // Ищем кости по именам, которые игра использует в Awake
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "chestLower")
                    _centerTargetBone = t;
                else if (t.name == "hip" && _centerTargetBone == null)
                    _centerTargetBone = t; // Фаллбэк на таз
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

            // Опрашиваем сенсоры 4 раза в секунду, чтобы не грузить CPU
            _sensorTimer += dt;
            if (_sensorTimer >= 0.25f)
            {
                _sensorTimer = 0f;
                EvaluateSensors();
            }

            // Универсальный тормоз: высокий Arousal гасит рост Embarrassment (до -75% на пике)
            float arousalDampener = Mathf.Lerp(1.0f, 0.25f, Arousal / 100f);
            float effectiveTargetEmb = TargetEmbarrassment * arousalDampener;

            // Плавное изменение шкал с учетом демпфера и конфигов BepInEx
            float embSpeed = (effectiveTargetEmb > Embarrassment)
                ? Plugin.BaseGainSpeed.Value * arousalDampener
                : Plugin.BaseDecaySpeed.Value;

            Embarrassment = Mathf.MoveTowards(Embarrassment, effectiveTargetEmb, embSpeed * dt);

            // Мягкое остывание возбуждения в рамках MVP (будет расширено на Фазе 2)
            Arousal = Mathf.MoveTowards(Arousal, 0f, 4.0f * dt);

            // Заготовка проверки для позы прикрывания руками
            bool shouldCover = (_isTopNaked || _isBottomNaked) && Embarrassment >= CoverThreshold;
            if (shouldCover != _isCoveringSelf)
            {
                _isCoveringSelf = shouldCover;
                // Тут в Фазе 1.5 будет хук подмены Idle-анимации
            }
        }

        private void LateUpdate()
        {
            // Применяем вес блендшейпа в LateUpdate поверх ванильного аниматора мимики
            if (_customization.body != null && _ashamedBlendShapeIndex >= 0)
            {
                // Смущение плавно переводится в вес блендшейпа (макс 80 из 100, чтобы лицо не перекашивало)
                float weight = Mathf.Clamp(Embarrassment * 0.8f, 0f, 80f);
                _customization.body.SetBlendShapeWeight(_ashamedBlendShapeIndex, weight);
            }
        }

        private void EvaluateSensors()
        {
            // --- TOPLESS CHECK ---
            // Has item in armor slot?
            bool hasArmor = _customization.armor != null;
            // Is the armor GameObject actually active?
            bool armorActive = hasArmor && _customization.armor.gameObject.activeInHierarchy;

            bool hasBra = _customization.bra != null;
            bool braActive = hasBra && _customization.bra.gameObject.activeInHierarchy;

            // Logic: Topless if NO armor is active AND NO bra is active
            _isTopNaked = !armorActive && !braActive;


            // --- BOTTOMLESS CHECK ---
            bool hasLeggings = _customization.leggings != null;
            bool leggingsActive = hasLeggings && _customization.leggings.gameObject.activeInHierarchy;

            bool hasPanties = _customization.panties != null;
            bool pantiesActive = hasPanties && _customization.panties.gameObject.activeInHierarchy;

            // Logic: Bottomless if NO leggings active AND NO panties active
            _isBottomNaked = !leggingsActive && !pantiesActive;


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

            // MVP target calculation based on state
            float target = 0f;
            if (_isTopNaked) target += 35f;
            if (_isBottomNaked) target += 40f;

            if ((_isTopNaked || _isBottomNaked) && _isLookedAtByCamera)
            {
                target += _canSeeCamera ? 35f : 5f;
            }

            TargetEmbarrassment = Mathf.Clamp(target, 0f, 100f);
        }

        private void OnGUI()
        {
            if (!Plugin.ShowDebugOverlay.Value) return;

            Camera cam = Camera.main;
            if (cam == null || _headBone == null) return;
            if (Vector3.Distance(cam.transform.position, _headBone.position) > 5f) return;

            Vector3 screenPos = cam.WorldToScreenPoint(_headBone.position + Vector3.up * 0.3f);
            if (screenPos.z <= 0) return;

            float y = Screen.height - screenPos.y;

            // Gather technical telemetry for Top/Bottom slots
            string armorStr = _customization.armor != null ? $"Exist(Act:{_customization.armor.gameObject.activeInHierarchy})" : "Null";
            string braStr = _customization.bra != null ? $"Exist(Act:{_customization.bra.gameObject.activeInHierarchy})" : "Null";
            string legsStr = _customization.leggings != null ? $"Exist(Act:{_customization.leggings.gameObject.activeInHierarchy})" : "Null";
            string pantyStr = _customization.panties != null ? $"Exist(Act:{_customization.panties.gameObject.activeInHierarchy})" : "Null";

            string text = $"{gameObject.name}\n" +
                          $"Emb: {Embarrassment:F1} (T:{TargetEmbarrassment:F0}) | Ar: {Arousal:F1}\n" +
                          $"TopState -> Armor: {armorStr} | Bra: {braStr} -> [TopNaked: {_isTopNaked}]\n" +
                          $"BotState -> Legs: {legsStr} | Panty: {pantyStr} -> [BotNaked: {_isBottomNaked}]\n" +
                          $"CamLook: {_isLookedAtByCamera} | SeesCam: {_canSeeCamera}";

            // Slightly wider box to fit telemetry
            GUI.Box(new Rect(screenPos.x - 170, y - 65, 340, 105), text);
        }

        // Публичные методы изменения шкал для внешних триггеров
        public void AddArousal(float amount) => Arousal = Mathf.Clamp(Arousal + amount, 0f, 100f);
        public void AddEmbarrassmentImpulse(float amount) => Embarrassment = Mathf.Clamp(Embarrassment + amount, 0f, 100f);

    }
}
