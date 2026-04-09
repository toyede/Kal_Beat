using System.Collections;
using UnityEngine;

namespace KalBeat.Combat
{
    public sealed class CombatReadyMarkView : MonoBehaviour
    {
        [SerializeField] private float pulseScaleMultiplier = 1.18f;

        private MeshRenderer meshRenderer;
        private Material runtimeMaterial;
        private Coroutine activeShowRoutine;
        private Vector3 baseScale = Vector3.one;

        private void Awake()
        {
            TryAssignRenderer();
            EnsureMaterial();
            Hide();
        }

        public void Show(float durationSec, Color color, Vector2 size)
        {
            TryAssignRenderer();
            EnsureMaterial();
            if (runtimeMaterial == null)
            {
                return;
            }

            baseScale = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), 1f);
            transform.localScale = baseScale;
            runtimeMaterial.color = color;

            if (activeShowRoutine != null)
            {
                StopCoroutine(activeShowRoutine);
            }

            activeShowRoutine = StartCoroutine(ShowRoutine(durationSec));
        }

        public void Hide()
        {
            if (activeShowRoutine != null)
            {
                StopCoroutine(activeShowRoutine);
                activeShowRoutine = null;
            }

            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }
        }

        private IEnumerator ShowRoutine(float durationSec)
        {
            if (meshRenderer == null)
            {
                yield break;
            }

            meshRenderer.enabled = true;
            float safeDuration = Mathf.Max(0.01f, durationSec);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / safeDuration);
                float pulse = Mathf.Lerp(1f, pulseScaleMultiplier, Mathf.Sin(normalized * Mathf.PI));
                transform.localScale = baseScale * pulse;
                yield return null;
            }

            transform.localScale = baseScale;
            meshRenderer.enabled = false;
            activeShowRoutine = null;
        }

        private void LateUpdate()
        {
            if (meshRenderer == null || !meshRenderer.enabled)
            {
                return;
            }

            Camera currentCamera = Camera.main;
            if (currentCamera == null)
            {
                return;
            }

            transform.forward = currentCamera.transform.forward;
        }

        private void TryAssignRenderer()
        {
            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
            }
        }

        private void EnsureMaterial()
        {
            if (meshRenderer == null || runtimeMaterial != null)
            {
                return;
            }

            Shader targetShader = Shader.Find("Unlit/Color");
            if (targetShader == null)
            {
                targetShader = Shader.Find("Sprites/Default");
            }

            if (targetShader == null)
            {
                return;
            }

            runtimeMaterial = new Material(targetShader);
            runtimeMaterial.color = Color.white;
            meshRenderer.sharedMaterial = runtimeMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (runtimeMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(runtimeMaterial);
            }
            else
            {
                DestroyImmediate(runtimeMaterial);
            }
        }
    }
}
