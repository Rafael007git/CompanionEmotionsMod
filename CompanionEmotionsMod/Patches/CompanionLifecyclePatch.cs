using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using FurnitureAnimationsMod;

namespace CompanionEmotionsMod.Patches
{
    public class CastleWanderTracker : MonoBehaviour
    {
        private Companion _companion;
        private NavMeshAgent _agent;
        private CharacterCustomization _customization;

        private Furniture _targetFurniture;
        private Furniture _activeFurniture;
        private bool _isUsingFurniture;
        private bool _extendedForAudio;
        private float _routineTimer;
        private float _standUpDelayTimer;
        private float _stuckTimer;
        private Vector3 _destinationPos;
        private Vector3 _lastCheckPos;
        private Vector3 _startWalkPos;

        private static bool _furnitureObstaclesInitialized;

        public string DebugStatus { get; private set; } = "Init";
        public bool IsWandering => _agent != null && _agent.enabled && (_agent.hasPath || _agent.pathPending);

        private void Awake()
        {
            _companion = GetComponent<Companion>();
            _agent = GetComponent<NavMeshAgent>();
            _customization = GetComponent<CharacterCustomization>();
            _routineTimer = 5f;
        }

        public void Tick(float deltaTime)
        {
            if (_companion == null || _agent == null || _customization == null) return;

            // Инициализируем NavMeshObstacle для всей мебели в замке один раз при входе в сцену
            if (!_furnitureObstaclesInitialized && Global.code != null && Global.code.interactableFurnitures != null)
            {
                EnsureFurnitureObstacles();
            }

            // 0. Пауза стабилизации после вставания с мебели (убирает подёргивание анимации)
            if (_standUpDelayTimer > 0f)
            {
                _standUpDelayTimer -= deltaTime;
                DebugStatus = "Stabilizing stand up...";
                return;
            }

            // 1. Удерживаем состояние использования мебели
            if (_isUsingFurniture)
            {
                _routineTimer -= deltaTime;
                if (_routineTimer > 0f)
                {
                    string furnName = _activeFurniture != null ? _activeFurniture.name : "Furniture";
                    DebugStatus = $"Using {furnName} ({_routineTimer:F0}s)";
                    return;
                }

                // Если музыка ещё играет — доигрываем текущий трек до конца
                if (!_extendedForAudio)
                {
                    float remainingAudio = FurnitureAnimAPI.GetRemainingAudioTime(_customization);
                    if (remainingAudio > 0.5f)
                    {
                        _extendedForAudio = true;
                        _routineTimer = remainingAudio + 0.05f;
                        string furnName = _activeFurniture != null ? _activeFurniture.name : "Furniture";
                        DebugStatus = $"Finishing song at {furnName} ({_routineTimer:F0}s)";
                        Plugin.Log.LogInfo($"[CastleAI] {gameObject.name} продлевает выступление на {_routineTimer:F1}с, чтобы доиграть трек.");
                        return;
                    }
                }

                if (_activeFurniture != null && !CanLeaveFurniture(_activeFurniture))
                {
                    ResetRoutineTimer();
                    return;
                }

                if (_activeFurniture != null)
                {
                    try { _activeFurniture.QuitInteractWithOnlyPoses(_customization); }
                    catch { _customization.interactingObject = null; }
                }

                FurnitureAnimAPI.StopCompanionAnimation(_customization);
                _activeFurniture = null;
                _isUsingFurniture = false;
                _extendedForAudio = false;

                // Даём аниматору 0.85с спокойно завершить выход из позы перед включением NavMeshAgent
                _standUpDelayTimer = 0.3f;
                _routineTimer = Random.Range(1f, 5f);
                DebugStatus = "Standing up...";
                return;
            }

            // 2. Вне мебели — следим за NavMeshAgent
            if (!_agent.enabled) _agent.enabled = true;

            if (_agent.isOnNavMesh)
            {
                _agent.nextPosition = transform.position;
            }

            if (!_agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit snapHit, 5.0f, NavMesh.AllAreas))
                    _agent.Warp(snapHit.position);
                else
                {
                    DebugStatus = "Off NavMesh!";
                    return;
                }
            }

            if (_companion.movingToTarget != null || _companion.target != null)
            {
                _targetFurniture = null;
                _stuckTimer = 0f;
                DebugStatus = "Following / Combat";
                return;
            }

            // 3. Движение пешком к цели
            if (_agent.pathPending || _agent.hasPath)
            {
                float realDist = Vector3.Distance(transform.position, _destinationPos);
                string goalName = _targetFurniture != null ? _targetFurniture.name : "Point";
                DebugStatus = $"To {goalName} (Dist:{realDist:F1}m)";

                if (realDist <= 2.4f)
                {
                    _companion.Stop();
                    _stuckTimer = 0f;

                    if (_targetFurniture != null)
                    {
                        if (_targetFurniture.user == null)
                        {
                            _targetFurniture.InteractWithOnlyPoses(_customization);
                            FurnitureAnimAPI.PlayRandomPoseForCompanion(_targetFurniture, _customization);
                            _activeFurniture = _targetFurniture;
                            _isUsingFurniture = true;
                            _extendedForAudio = false;
                        }
                        _targetFurniture = null;
                    }

                    ResetRoutineTimer();
                    return;
                }

                // Детектор застревания: если за 4 секунды почти не сдвинулись — сбрасываем маршрут
                if ((transform.position - _lastCheckPos).sqrMagnitude < 0.03f)
                {
                    _stuckTimer += deltaTime;
                    if (_stuckTimer >= 4f)
                    {
                        _companion.Stop();
                        _targetFurniture = null;
                        _stuckTimer = 0f;
                        _routineTimer = 2f;
                        DebugStatus = "Stuck -> Reset";
                    }
                }
                else
                {
                    _stuckTimer = 0f;
                    _lastCheckPos = transform.position;
                }
                return;
            }

            // 4. Отсчёт таймера в покое (Idle)
            _routineTimer -= deltaTime;
            if (_routineTimer > 0f)
            {
                DebugStatus = $"Idle (Next:{_routineTimer:F0}s)";
                return;
            }

            // 5. Выбор новой цели (50% подойти к мебели, 50% пройтись по залу)
            if (Random.value < 0.50f && TryWalkToRandomFurniture())
            {
                _stuckTimer = 0f;
                _lastCheckPos = transform.position;
                ResetRoutineTimer();
                return;
            }

            if (TryWanderToRandomPoint())
            {
                _targetFurniture = null;
                _stuckTimer = 0f;
                _lastCheckPos = transform.position;
                ResetRoutineTimer();
            }
            else
            {
                DebugStatus = "No Valid Path!";
                _routineTimer = 4f;
            }
        }

        private void ResetRoutineTimer()
        {
            float min = Plugin.MinRoutineInterval != null ? Plugin.MinRoutineInterval.Value : 60f;
            float max = Plugin.MaxRoutineInterval != null ? Plugin.MaxRoutineInterval.Value : 180f;
            _routineTimer = Random.Range(Mathf.Min(min, max), Mathf.Max(min, max));
        }

        private bool CanLeaveFurniture(Furniture f)
        {
            if (f.dontRandomPose) return false;
            return true;
        }

        /// <summary>
        /// Вырезает контуры расставленной мебели из NavMesh, чтобы NPC обходили кресла и столы по дуге.
        /// </summary>
        private static void EnsureFurnitureObstacles()
        {
            _furnitureObstaclesInitialized = true;
            int carvedCount = 0;

            foreach (Transform item in Global.code.interactableFurnitures.items)
            {
                if (!item || !item.gameObject.activeInHierarchy) continue;
                if (item.GetComponent<NavMeshObstacle>() != null) continue;

                Collider col = item.GetComponentInChildren<Collider>();
                if (col == null || col.isTrigger) continue;

                Bounds b = col.bounds;
                // Игнорируем плоские ковры и слишком огромные архитектурные полы
                if (b.size.y < 0.25f || b.size.x > 6f || b.size.z > 6f) continue;

                NavMeshObstacle obs = item.gameObject.AddComponent<NavMeshObstacle>();
                obs.shape = NavMeshObstacleShape.Box;
                obs.center = item.InverseTransformPoint(b.center);
                obs.size = new Vector3(
                    Mathf.Clamp(b.size.x / Mathf.Max(0.1f, item.lossyScale.x), 0.3f, 4f),
                    Mathf.Clamp(b.size.y / Mathf.Max(0.1f, item.lossyScale.y), 0.3f, 3f),
                    Mathf.Clamp(b.size.z / Mathf.Max(0.1f, item.lossyScale.z), 0.3f, 4f)
                                );
                obs.carving = true;
                obs.carveOnlyStationary = true;
                carvedCount++;
            }

            if (carvedCount > 0)
            {
                Plugin.Log.LogInfo($"[CastleAI] Вырезано препятствий в NavMesh для {carvedCount} предметов мебели.");
            }
        }

        /// <summary>
        /// Виртуальная проверка маршрута капсулой: прокатывает габариты тела (включая уровень груди и плеч)
        /// по всем отрезкам NavMeshPath, отсеивая маршруты с застреванием в выступах стен и мебели.
        /// </summary>
        private bool IsPathClearForBody(NavMeshPath path, Furniture allowedTargetFurniture = null)
        {
            if (path == null || path.corners == null || path.corners.Length < 2)
                return false;

            float bodyRadius = 0.30f;
            Vector3 bottomOffset = Vector3.up * 0.48f; // Выше ковриков и мелких порогов
            Vector3 topOffset = Vector3.up * 1.55f;    // Уровень плеч и головы (ловит нависающие выступы стен!)

            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                Vector3 segStart = path.corners[i];
                Vector3 segEnd = path.corners[i + 1];
                Vector3 dir = segEnd - segStart;
                float dist = dir.magnitude;

                if (dist < 0.05f) continue;
                dir /= dist;

                if (Physics.CapsuleCast(
                    segStart + bottomOffset,
                    segStart + topOffset,
                    bodyRadius,
                    dir,
                    out RaycastHit hit,
                    dist,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                {
                    // Игнорируем коллайдеры самой компаньонки и игрока
                    if (hit.transform.IsChildOf(transform)) continue;
                    if (Player.code != null && hit.transform.IsChildOf(Player.code.transform)) continue;

                    // Игнорируем других персонажей (они сами разойдутся)
                    if (hit.transform.GetComponentInParent<Companion>() != null) continue;

                    // Если упёрлись в целевую мебель на финальном отрезке пути (в пределах 1.6м до конца) — это нормально
                    if (allowedTargetFurniture != null &&
                        hit.transform.IsChildOf(allowedTargetFurniture.transform) &&
                        (dist - hit.distance) <= 1.6f)
                    {
                        continue;
                    }

                    // Наткнулись на стену, каменный выступ или постороннюю мебель — маршрут бракуем
                    return false;
                }
            }

            return true;
        }

        private bool TryWalkToRandomFurniture()
        {
            if (Global.code == null || Global.code.interactableFurnitures == null) return false;

            List<(Furniture furn, NavMeshPath path, Vector3 destPos)> candidates = new List<(Furniture, NavMeshPath, Vector3)>();

            foreach (Transform item in Global.code.interactableFurnitures.items)
            {
                if (!item || !item.gameObject.activeInHierarchy) continue;

                float directDist = Vector3.Distance(transform.position, item.position);
                if (directDist > 20f || directDist < 2.8f) continue;

                Furniture f = item.GetComponent<Furniture>();
                if (f != null && f.user == null)
                {
                    bool hasValidPoses = (f.poses != null && f.poses.items.Count > 0) || FurnitureAnimAPI.HasCustomAnimations(f);
                    if (!hasValidPoses) continue;
                    if (item.GetComponent<Mirror>() != null) continue;
                    if (item.GetComponent<LingerieShop>() != null) continue;

                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.SamplePosition(item.position, out NavMeshHit hit, 2.2f, NavMesh.AllAreas) &&
                        Vector3.Distance(hit.position, item.position) <= 2.3f &&
                        _agent.CalculatePath(hit.position, path) &&
                        path.status == NavMeshPathStatus.PathComplete &&
                        IsPathClearForBody(path, f))
                    {
                        candidates.Add((f, path, hit.position));
                    }
                }
            }

            if (candidates.Count == 0) return false;

            var chosen = candidates[Random.Range(0, candidates.Count)];
            _targetFurniture = chosen.furn;
            _destinationPos = chosen.destPos;

            UnfreezePhysics();
            _agent.nextPosition = transform.position;
            _startWalkPos = transform.position;
            _agent.SetPath(chosen.path);
            return true;
        }

        private bool TryWanderToRandomPoint()
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(4.0f, 9.0f);
                Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                    continue;

                NavMeshPath path = new NavMeshPath();
                if (!_agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                    continue;

                // Проверяем весь маршрут виртуальной капсулой до начала движения
                if (!IsPathClearForBody(path))
                    continue;

                UnfreezePhysics();
                _agent.nextPosition = transform.position;
                _startWalkPos = transform.position;
                _destinationPos = hit.position;

                return _agent.SetPath(path);
            }

            return false;
        }

        public void UnfreezePhysics()
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }

            _agent.updatePosition = false;
            _agent.updateRotation = false;
            if (_agent.speed < 1.5f) _agent.speed = 3.5f;
            _agent.isStopped = false;
        }

        // Сбрасываем кэш препятствий при смене локации
        private void OnDestroy()
        {
            _furnitureObstaclesInitialized = false;
        }
    }

    [HarmonyPatch(typeof(Companion), "CS")]
    public static class CompanionCastleAIPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Companion __instance)
        {
            if (Plugin.EnableCastleLifeFix != null && !Plugin.EnableCastleLifeFix.Value) return true;
            if (__instance == null || __instance._ID == null || __instance._ID.isDead) return true;
            if (Global.code == null || Global.code.onGUI || Global.code.curlocation == null) return true;

            if (Global.code.curlocation.locationType == LocationType.home)
            {
                CastleWanderTracker tracker = __instance.GetComponent<CastleWanderTracker>();
                if (tracker != null && tracker.IsWandering)
                {
                    return false;
                }
            }

            return true;
        }
    }

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
                    if (anim != null && anim.enabled)
                    {
                        anim.speed = 1f;

                        CastleWanderTracker tracker = comp.GetComponent<CastleWanderTracker>();
                        if (tracker != null)
                        {
                            tracker.UnfreezePhysics();
                        }
                        else
                        {
                            Rigidbody rb = comp.GetComponent<Rigidbody>();
                            if (rb != null)
                            {
                                rb.isKinematic = false;
                                rb.useGravity = true;
                                rb.constraints = RigidbodyConstraints.FreezeRotation;
                            }
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(Companion), nameof(Companion.InteractWithFurniture))]
    public static class CompanionFurniturePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Companion __instance)
        {
            if (Plugin.EnableCastleLifeFix != null && !Plugin.EnableCastleLifeFix.Value) return true;
            return false;
        }
    }
}
