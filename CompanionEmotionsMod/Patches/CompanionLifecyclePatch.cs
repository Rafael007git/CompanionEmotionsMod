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
        
        private float _routineTimer;
        private float _stuckTimer;
        private Vector3 _destinationPos;
        private Vector3 _lastCheckPos;
        private Vector3 _startWalkPos;


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

            // 1. Жёстко удерживаем состояние использования мебели по нашему флагу
            if (_isUsingFurniture)
            {
                _routineTimer -= deltaTime;
                if (_routineTimer > 0f)
                {
                    string furnName = _activeFurniture != null ? _activeFurniture.name : "Furniture";
                    DebugStatus = $"Using {furnName} ({_routineTimer:F0}s)";
                    return;
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
                _routineTimer = 3f;
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
                        Plugin.Log.LogWarning($"[WANDER_DEBUG] Arrived at '{_targetFurniture.name}'! MyPos={transform.position}, DestPos={_destinationPos}, Dist={realDist:F2}m");
                        if (_targetFurniture.user == null)
                        {
                            _targetFurniture.InteractWithOnlyPoses(_customization);
                            FurnitureAnimAPI.PlayRandomPoseForCompanion(_targetFurniture, _customization);
                            _activeFurniture = _targetFurniture;
                            _isUsingFurniture = true;
                        }
                        _targetFurniture = null;
                    }

                    ResetRoutineTimer();
                    return;
                }

                if ((transform.position - _lastCheckPos).sqrMagnitude < 0.02f)
                {
                    _stuckTimer += deltaTime;
                    if (_stuckTimer >= 7f)
                    {
                        _companion.Stop();
                        _targetFurniture = null;
                        _stuckTimer = 0f;
                        _routineTimer = 3f;
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
            /*
            if (Random.value < 0.50f && TryWalkToRandomFurniture())
            {
                _stuckTimer = 0f;
                _lastCheckPos = transform.position;
                return;
            }
            */

            if (TryWanderToRandomPoint())
            {
                _targetFurniture = null;
                _stuckTimer = 0f;
                _lastCheckPos = transform.position;
                ResetRoutineTimer(); // <--- БЕЗ ЭТОЙ СТРОЧКИ ОНА ПЕРЕЗАПУСКАЕТ ПУТЬ КАЖДЫЙ КАДР!
            }
            else
            {
                DebugStatus = "No Valid Path!";
                _routineTimer = 5f;
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

        private bool TryWalkToRandomFurniture()
        {
            if (Global.code == null || Global.code.interactableFurnitures == null) return false;

            List<(Furniture furn, NavMeshPath path, Vector3 destPos)> candidates = new List<(Furniture, NavMeshPath, Vector3)>();

            foreach (Transform item in Global.code.interactableFurnitures.items)
            {
                if (!item || !item.gameObject.activeInHierarchy) continue;

                float directDist = Vector3.Distance(transform.position, item.position);
                // Игнорируем слишком далёкую мебель и ту, что уже стоит вплотную (чтобы был реальный шаг)
                if (directDist > 20f || directDist < 2.8f) continue;

                Furniture f = item.GetComponent<Furniture>();
                if (f != null && f.user == null)
                {
                    bool hasValidPoses = (f.poses != null && f.poses.items.Count > 0) || FurnitureAnimAPI.HasCustomAnimations(f);
                    if (!hasValidPoses) continue;

                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.SamplePosition(item.position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas) &&
                        Vector3.Distance(hit.position, item.position) <= 2.2f &&
                        _agent.CalculatePath(hit.position, path) &&
                        path.status == NavMeshPathStatus.PathComplete)
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
            Plugin.Log.LogWarning($"[WANDER_DEBUG] Starting walk to '{chosen.furn.name}' at {chosen.destPos} (StartDist={Vector3.Distance(transform.position, chosen.destPos):F2}m)");
            _agent.SetPath(chosen.path);
            return true;
        }

        private bool TryWanderToRandomPoint()
        {
            int sampleFails = 0;
            int pathFails = 0;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(4.0f, 9.0f);
                Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    sampleFails++;
                    continue;
                }

                NavMeshPath path = new NavMeshPath();
                if (!_agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    pathFails++;
                    continue;
                }

                UnfreezePhysics();
                _agent.nextPosition = transform.position;
                _startWalkPos = transform.position;
                _destinationPos = hit.position;

                bool setOk = _agent.SetPath(path);
                DebugStatus = $"SetPath={setOk} | HasPath={_agent.hasPath} | Pending={_agent.pathPending}";
                Plugin.Log.LogInfo($"[CastleAI_Diag] {gameObject.name}: SetPath={setOk}, HasPath={_agent.hasPath}, Pending={_agent.pathPending}, OnNavMesh={_agent.isOnNavMesh}, Stopped={_agent.isStopped}");
                return setOk;
            }

            DebugStatus = $"NoPath (SampleFail:{sampleFails}, PathFail:{pathFails})";
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
                    // Если anim.enabled == false, персонаж осознанно заморожен через Keep Pose в Advanced Free Pose
                    if (anim != null && anim.enabled)
                    {
                        anim.speed = 1f;

                        // Размораживаем физику и навигацию после режима позирования
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
            // Полностью отключаем ванильный телепорт на случайную мебель при загрузке сцены,
            // чтобы девушки не скакали по витринам и не исчезали через SetActive(false)
            return false;
        }
    }

}
