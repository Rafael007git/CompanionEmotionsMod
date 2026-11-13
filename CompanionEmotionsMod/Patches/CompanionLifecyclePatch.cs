using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

namespace CompanionEmotionsMod.Patches
{
    public class CastleWanderTracker : MonoBehaviour
    {
        private Companion _companion;
        private NavMeshAgent _agent;
        private CharacterCustomization _customization;

        private float _routineTimer;
        private float _stuckTimer;
        private Vector3 _lastCheckPos;

        public bool IsWandering => _agent != null && _agent.enabled && _agent.hasPath;

        private void Awake()
        {
            _companion = GetComponent<Companion>();
            _agent = GetComponent<NavMeshAgent>();
            _customization = GetComponent<CharacterCustomization>();
            // Первый переход запускаем быстрее (через 5-15 сек), чтобы сразу проверить в игре, затем 60-180 сек
            _routineTimer = Random.Range(5f, 15f);
        }

        public void Tick(float deltaTime)
        {
            if (_companion == null || _agent == null || _customization == null) return;
            if (!_agent.enabled || !_agent.isOnNavMesh) return;

            // Если компаньонка следует за игроком или ведёт бой — не мешаем
            if (_companion.movingToTarget != null || _companion.target != null)
            {
                _stuckTimer = 0f;
                return;
            }

            // Если сейчас идёт к точке маршрута
            if (_agent.hasPath)
            {
                if (_agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
                {
                    _companion.Stop();
                    _stuckTimer = 0f;
                    ResetRoutineTimer();
                    return;
                }

                // Защита от застревания
                if ((transform.position - _lastCheckPos).sqrMagnitude < 0.02f)
                {
                    _stuckTimer += deltaTime;
                    if (_stuckTimer >= 8f)
                    {
                        _companion.Stop();
                        _stuckTimer = 0f;
                        _routineTimer = Random.Range(3f, 8f);
                    }
                }
                else
                {
                    _stuckTimer = 0f;
                    _lastCheckPos = transform.position;
                }
                return;
            }

            // Отсчёт таймера смены занятия
            _routineTimer -= deltaTime;
            if (_routineTimer > 0f) return;

            // Если сидит на мебели — проверяем, можно ли уйти самостоятельно
            if (_customization.interactingObject != null)
            {
                Furniture currentFurniture = _customization.interactingObject.GetComponent<Furniture>();
                if (currentFurniture != null && !CanLeaveFurniture(currentFurniture))
                {
                    ResetRoutineTimer();
                    return;
                }

                // Безопасно встаём с мебели
                if (currentFurniture != null)
                {
                    try { currentFurniture.QuitInteractWithOnlyPoses(_customization); }
                    catch { _customization.interactingObject = null; }
                }
            }

            // С вероятностью 40% садимся на другую свободную мебель, иначе просто гуляем по залу
            if (Random.value < 0.40f && TryUseRandomFurniture())
            {
                ResetRoutineTimer();
                return;
            }

            if (TryWanderToRandomPoint())
            {
                _stuckTimer = 0f;
                _lastCheckPos = transform.position;
                ResetRoutineTimer();
            }
            else
            {
                _routineTimer = Random.Range(5f, 10f);
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
            if (_customization.anim != null && !_customization.anim.enabled) return false;
            return true;
        }

        private bool TryWanderToRandomPoint()
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized * Random.Range(4f, 10f);
                Vector3 candidate = transform.position + new Vector3(circle.x, 0f, circle.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    NavMeshPath path = new NavMeshPath();
                    if (_agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        _agent.isStopped = false;
                        _agent.SetDestination(hit.position);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool TryUseRandomFurniture()
        {
            if (Global.code == null || Global.code.interactableFurnitures == null) return false;

            List<Furniture> available = new List<Furniture>();
            foreach (Transform item in Global.code.interactableFurnitures.items)
            {
                if (!item || !item.gameObject.activeInHierarchy) continue;

                Furniture f = item.GetComponent<Furniture>();
                if (f != null && f.user == null && f.poses != null && f.poses.items.Count > 0)
                {
                    NavMeshPath path = new NavMeshPath();
                    if (NavMesh.SamplePosition(item.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas) &&
                        _agent.CalculatePath(hit.position, path) &&
                        path.status == NavMeshPathStatus.PathComplete)
                    {
                        available.Add(f);
                    }
                }
            }

            if (available.Count == 0) return false;
            Furniture chosen = available[Random.Range(0, available.Count)];
            chosen.InteractWithOnlyPoses(_customization);
            return true;
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
                if (tracker == null)
                {
                    tracker = __instance.gameObject.AddComponent<CastleWanderTracker>();
                }

                tracker.Tick(1f);

                // Если компаньонка сейчас гуляет по замку, отменяем оригинальный CS(), чтобы он не вызвал this.Stop()
                if (tracker.IsWandering)
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
            if (Global.code == null || Global.code.interactableFurnitures == null) return false;

            List<Furniture> validFurniture = new List<Furniture>();

            foreach (Transform item in Global.code.interactableFurnitures.items)
            {
                if (!item || !item.gameObject.activeInHierarchy) continue;

                Furniture f = item.GetComponent<Furniture>();
                // Строгий фильтр: только мебель, у которой реально есть хотя бы одна поза
                if (f != null && f.poses != null && f.poses.items.Count > 0)
                {
                    // Снимаем ошибочный запрет от AdvancedFreePoseMode
                    f.notInteractableByCompanion = false;

                    if (f.user == null)
                    {
                        validFurniture.Add(f);
                    }
                }
            }

            if (validFurniture.Count > 0)
            {
                Furniture chosen = validFurniture[Random.Range(0, validFurniture.Count)];
                chosen.InteractWithOnlyPoses(__instance.customization);
            }

            // Отменяем оригинальный метод, чтобы лишние компаньонки не исчезали через SetActive(false)
            return false;
        }
    }

}
