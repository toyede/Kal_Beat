using System;
using System.Collections;
using System.Collections.Generic;
using KalBeat.Charting;
using UnityEngine;

namespace KalBeat.Combat
{
    public sealed class RangedAttackVisualView : MonoBehaviour
    {
        private const string DefaultAnchorRootName = "RangedAnchorRoot";

        [SerializeField] private Transform rangedAnchorRoot;
        [SerializeField] private Transform[] rangedAnchors;
        [SerializeField] private bool createRuntimeFallbackAnchors = true;
        [SerializeField] private int fallbackAnchorCount = 8;
        [SerializeField] private Vector3 fallbackAnchorCenterLocal = new Vector3(0f, 2.1f, 0f);
        [SerializeField] private float fallbackAnchorRadius = 1.35f;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Color baseCircleColor = new Color(0.15f, 0.95f, 1f, 0.9f);
        [SerializeField] private Color stackedCircleColor = new Color(0.88f, 0.25f, 1f, 1f);
        [SerializeField] private Color beamColor = new Color(0.2f, 0.9f, 1f, 1f);
        [SerializeField] private float circleRadius = 0.55f;
        [SerializeField] private float circleLineWidth = 0.05f;
        [SerializeField] private int circleSegmentCount = 48;
        [SerializeField] private float stackedCircleScale = 1.18f;
        [SerializeField] private float beamDurationSec = 0.15f;
        [SerializeField] private float beamWidth = 0.18f;

        private readonly Dictionary<string, ActiveCircle> activeCircles = new Dictionary<string, ActiveCircle>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActiveProjectile> activeProjectiles = new Dictionary<string, ActiveProjectile>(StringComparer.Ordinal);
        private Material runtimeLineMaterial;

        public void ShowCallCircle(ResolvedCombatStep resolvedStep)
        {
            if (resolvedStep == null)
            {
                return;
            }

            Transform anchor = ResolveAnchor(resolvedStep.RangedAnchorIndex, resolvedStep.RangedCueIndex);
            if (anchor == null)
            {
                return;
            }

            string key = BuildCircleKey(resolvedStep);
            if (!activeCircles.TryGetValue(key, out ActiveCircle activeCircle))
            {
                activeCircle = new ActiveCircle(CreateCircle(anchor));
                activeCircles.Add(key, activeCircle);
            }

            activeCircle.RemainingShots++;
            activeCircle.View.SetVisualState(activeCircle.RemainingShots, baseCircleColor, stackedCircleColor, stackedCircleScale);
        }

        public void FireAndConsume(ResolvedCombatStep resolvedStep, Vector3 lockedTargetWorldPosition)
        {
            if (resolvedStep == null)
            {
                return;
            }

            Transform anchor = ResolveAnchor(resolvedStep.RangedAnchorIndex, resolvedStep.RangedCueIndex);
            if (anchor == null)
            {
                return;
            }

            StartCoroutine(FireBeamRoutine(anchor.position, lockedTargetWorldPosition));

            string key = BuildCircleKey(resolvedStep);
            if (!activeCircles.TryGetValue(key, out ActiveCircle activeCircle))
            {
                return;
            }

            activeCircle.RemainingShots = Mathf.Max(0, activeCircle.RemainingShots - 1);
            if (activeCircle.RemainingShots <= 0)
            {
                DestroyCircle(key, activeCircle);
                return;
            }

            activeCircle.View.SetVisualState(activeCircle.RemainingShots, baseCircleColor, stackedCircleColor, stackedCircleScale);
        }

        public void ShowCounterProjectile(ResolvedCombatStep resolvedStep, CombatDuelRoot duelRoot)
        {
            if (resolvedStep == null || duelRoot == null || !resolvedStep.HasProjectileData)
            {
                return;
            }

            if (resolvedStep.ProjectilePrefab == null)
            {
                Debug.LogWarning("Ranged counter projectile prefab is missing from ResolvedCombatStep.", this);
                return;
            }

            string key = BuildProjectileKey(resolvedStep);
            if (activeProjectiles.TryGetValue(key, out ActiveProjectile existingProjectile))
            {
                DestroyProjectile(key, existingProjectile);
            }

            Vector3 spawnWorldPosition = duelRoot.ConvertDuelLocal3DToWorld(resolvedStep.ProjectileSpawnLocalPos);
            Vector3 targetWorldPosition = duelRoot.ConvertDuelLocal3DToWorld(resolvedStep.ProjectileTargetPivotLocalPos);
            GameObject projectileObject = Instantiate(resolvedStep.ProjectilePrefab, spawnWorldPosition, Quaternion.identity);
            Vector3 direction = targetWorldPosition - spawnWorldPosition;
            if (direction.sqrMagnitude > 0.0001f)
            {
                projectileObject.transform.forward = direction.normalized;
            }

            Coroutine routine = StartCoroutine(CounterProjectileRoutine(
                key,
                projectileObject,
                spawnWorldPosition,
                targetWorldPosition,
                resolvedStep));
            activeProjectiles.Add(key, new ActiveProjectile(projectileObject, routine));
        }

        public void ResolveCounterProjectile(ResolvedCombatStep resolvedStep, CombatJudgementGrade grade)
        {
            if (resolvedStep == null ||
                (grade != CombatJudgementGrade.Perfect &&
                 grade != CombatJudgementGrade.Good &&
                 grade != CombatJudgementGrade.Miss))
            {
                return;
            }

            string key = BuildProjectileKey(resolvedStep);
            if (!activeProjectiles.TryGetValue(key, out ActiveProjectile activeProjectile))
            {
                return;
            }

            DestroyProjectile(key, activeProjectile);
        }

        public void Clear()
        {
            foreach (KeyValuePair<string, ActiveCircle> pair in activeCircles)
            {
                if (pair.Value?.View != null)
                {
                    Destroy(pair.Value.View.gameObject);
                }
            }

            activeCircles.Clear();

            foreach (KeyValuePair<string, ActiveProjectile> pair in activeProjectiles)
            {
                if (pair.Value?.Routine != null)
                {
                    StopCoroutine(pair.Value.Routine);
                }

                if (pair.Value?.ProjectileObject != null)
                {
                    Destroy(pair.Value.ProjectileObject);
                }
            }

            activeProjectiles.Clear();
        }

        private RangedMagicCircleView CreateCircle(Transform anchor)
        {
            GameObject circleObject = new GameObject("RangedMagicCircle");
            circleObject.transform.SetParent(anchor, false);
            circleObject.transform.localPosition = Vector3.zero;
            circleObject.transform.localRotation = Quaternion.identity;
            circleObject.transform.localScale = Vector3.one;

            RangedMagicCircleView circleView = circleObject.AddComponent<RangedMagicCircleView>();
            circleView.Initialize(GetLineMaterial(), circleRadius, circleLineWidth, circleSegmentCount);
            circleView.SetVisualState(1, baseCircleColor, stackedCircleColor, stackedCircleScale);
            return circleView;
        }

        private IEnumerator FireBeamRoutine(Vector3 origin, Vector3 target)
        {
            GameObject beamObject = new GameObject("RangedBeam");
            LineRenderer lineRenderer = beamObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, origin);
            lineRenderer.SetPosition(1, target);
            lineRenderer.startWidth = beamWidth;
            lineRenderer.endWidth = beamWidth;
            lineRenderer.material = GetLineMaterial();

            float safeDuration = Mathf.Max(0.01f, beamDurationSec);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(elapsed / safeDuration);
                Color fadedColor = new Color(beamColor.r, beamColor.g, beamColor.b, beamColor.a * alpha);
                lineRenderer.startColor = fadedColor;
                lineRenderer.endColor = fadedColor;
                yield return null;
            }

            Destroy(beamObject);
        }

        private IEnumerator CounterProjectileRoutine(
            string key,
            GameObject projectileObject,
            Vector3 spawnWorldPosition,
            Vector3 targetWorldPosition,
            ResolvedCombatStep resolvedStep)
        {
            float launchDelaySec = Mathf.Max(0f, resolvedStep.ProjectileLaunchSec - resolvedStep.HitSec);
            float elapsedDelaySec = 0f;
            while (elapsedDelaySec < launchDelaySec)
            {
                if (projectileObject == null)
                {
                    yield break;
                }

                elapsedDelaySec += Time.deltaTime;
                yield return null;
            }

            float travelDurationSec = Mathf.Max(0.0001f, resolvedStep.ProjectileContactSec - resolvedStep.ProjectileLaunchSec);
            float elapsedTravelSec = 0f;
            while (elapsedTravelSec < travelDurationSec)
            {
                if (projectileObject == null)
                {
                    yield break;
                }

                elapsedTravelSec += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTravelSec / travelDurationSec);
                projectileObject.transform.position = Vector3.Lerp(spawnWorldPosition, targetWorldPosition, t);
                yield return null;
            }

            if (projectileObject != null)
            {
                projectileObject.transform.position = targetWorldPosition;
            }

            if (activeProjectiles.TryGetValue(key, out ActiveProjectile activeProjectile))
            {
                activeProjectile.Routine = null;
            }
        }

        private Transform ResolveAnchor(int anchorIndex, int cueIndex)
        {
            EnsureAnchors();
            if (rangedAnchors == null || rangedAnchors.Length == 0)
            {
                return null;
            }

            int resolvedIndex = anchorIndex >= 0 ? anchorIndex : cueIndex;
            resolvedIndex = Mathf.Abs(resolvedIndex) % rangedAnchors.Length;
            return rangedAnchors[resolvedIndex];
        }

        private void EnsureAnchors()
        {
            if (HasValidAnchors())
            {
                return;
            }

            TryAssignAnchorRoot();
            if (rangedAnchorRoot != null && rangedAnchorRoot.childCount > 0)
            {
                List<Transform> children = new List<Transform>(rangedAnchorRoot.childCount);
                for (int i = 0; i < rangedAnchorRoot.childCount; i++)
                {
                    Transform child = rangedAnchorRoot.GetChild(i);
                    if (child != null)
                    {
                        children.Add(child);
                    }
                }

                children.Sort(CompareTransformsByName);
                rangedAnchors = children.ToArray();
                return;
            }

            if (createRuntimeFallbackAnchors)
            {
                CreateRuntimeFallbackAnchors();
            }
        }

        private bool HasValidAnchors()
        {
            if (rangedAnchors == null || rangedAnchors.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < rangedAnchors.Length; i++)
            {
                if (rangedAnchors[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryAssignAnchorRoot()
        {
            if (rangedAnchorRoot != null)
            {
                return;
            }

            Transform foundRoot = transform.Find(DefaultAnchorRootName);
            if (foundRoot != null)
            {
                rangedAnchorRoot = foundRoot;
            }
        }

        private void CreateRuntimeFallbackAnchors()
        {
            int safeCount = Mathf.Clamp(fallbackAnchorCount, 1, 32);
            GameObject rootObject = new GameObject(DefaultAnchorRootName);
            rootObject.transform.SetParent(transform, false);
            rootObject.transform.localPosition = Vector3.zero;
            rangedAnchorRoot = rootObject.transform;

            rangedAnchors = new Transform[safeCount];
            for (int i = 0; i < safeCount; i++)
            {
                float angle = i * Mathf.PI * 2f / safeCount;
                Vector3 localPosition = fallbackAnchorCenterLocal +
                                        new Vector3(Mathf.Cos(angle) * fallbackAnchorRadius, 0f, Mathf.Sin(angle) * fallbackAnchorRadius);

                GameObject anchorObject = new GameObject($"RangedAnchor_{i:00}");
                anchorObject.transform.SetParent(rangedAnchorRoot, false);
                anchorObject.transform.localPosition = localPosition;
                rangedAnchors[i] = anchorObject.transform;
            }
        }

        private Material GetLineMaterial()
        {
            if (lineMaterial != null)
            {
                return lineMaterial;
            }

            if (runtimeLineMaterial != null)
            {
                return runtimeLineMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                runtimeLineMaterial = new Material(shader);
            }

            return runtimeLineMaterial;
        }

        private void DestroyCircle(string key, ActiveCircle activeCircle)
        {
            activeCircles.Remove(key);
            if (activeCircle?.View != null)
            {
                Destroy(activeCircle.View.gameObject);
            }
        }

        private static string BuildCircleKey(ResolvedCombatStep resolvedStep)
        {
            return $"{resolvedStep.RangedPhraseId}:{resolvedStep.RangedAnchorIndex}";
        }

        private static string BuildProjectileKey(ResolvedCombatStep resolvedStep)
        {
            return $"{resolvedStep.RangedPhraseId}:{resolvedStep.RangedCueIndex}";
        }

        private void DestroyProjectile(string key, ActiveProjectile activeProjectile)
        {
            activeProjectiles.Remove(key);
            if (activeProjectile?.Routine != null)
            {
                StopCoroutine(activeProjectile.Routine);
            }

            if (activeProjectile?.ProjectileObject != null)
            {
                Destroy(activeProjectile.ProjectileObject);
            }
        }

        private static int CompareTransformsByName(Transform left, Transform right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return string.Compare(left.name, right.name, StringComparison.Ordinal);
        }

        private void OnDestroy()
        {
            Clear();
            if (runtimeLineMaterial != null)
            {
                Destroy(runtimeLineMaterial);
                runtimeLineMaterial = null;
            }
        }

        private void OnValidate()
        {
            fallbackAnchorCount = Mathf.Clamp(fallbackAnchorCount, 1, 32);
            fallbackAnchorRadius = Mathf.Max(0.01f, fallbackAnchorRadius);
            circleRadius = Mathf.Max(0.01f, circleRadius);
            circleLineWidth = Mathf.Max(0.001f, circleLineWidth);
            circleSegmentCount = Mathf.Max(12, circleSegmentCount);
            stackedCircleScale = Mathf.Max(1f, stackedCircleScale);
            beamDurationSec = Mathf.Max(0.01f, beamDurationSec);
            beamWidth = Mathf.Max(0.001f, beamWidth);
        }

        private sealed class ActiveCircle
        {
            public ActiveCircle(RangedMagicCircleView view)
            {
                View = view;
            }

            public RangedMagicCircleView View { get; }
            public int RemainingShots { get; set; }
        }

        private sealed class ActiveProjectile
        {
            public ActiveProjectile(GameObject projectileObject, Coroutine routine)
            {
                ProjectileObject = projectileObject;
                Routine = routine;
            }

            public GameObject ProjectileObject { get; }
            public Coroutine Routine { get; set; }
        }
    }
}
