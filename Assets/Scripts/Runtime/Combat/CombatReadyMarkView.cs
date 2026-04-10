using System.Collections;
using UnityEngine;

namespace KalBeat.Combat
{
    public sealed class CombatReadyMarkView : MonoBehaviour
    {
        [SerializeField] private float pulseScaleMultiplier = 1.18f;
        [SerializeField] private SpriteRenderer spriteRenderer;

        private Coroutine activeShowRoutine;
        private Vector3 baseScale = Vector3.one;

        private void Awake()
        {
            TryAssignRenderer();
            CacheBaseScale();
            Hide();
        }

        public void Show(float durationSec, Color color)
        {
            TryAssignRenderer();
            if (spriteRenderer == null)
            {
                return;
            }

            CacheBaseScale();
            transform.localScale = baseScale;
            spriteRenderer.color = color;

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

            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = false;
            }
        }

        private IEnumerator ShowRoutine(float durationSec)
        {
            if (spriteRenderer == null)
            {
                yield break;
            }

            spriteRenderer.enabled = true;
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
            spriteRenderer.enabled = false;
            activeShowRoutine = null;
        }

        private void LateUpdate()
        {
            if (spriteRenderer == null || !spriteRenderer.enabled)
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
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private void CacheBaseScale()
        {
            Vector3 currentScale = transform.localScale;
            baseScale = new Vector3(
                Mathf.Max(0.01f, currentScale.x),
                Mathf.Max(0.01f, currentScale.y),
                Mathf.Max(0.01f, currentScale.z));
        }

        private void OnValidate()
        {
            TryAssignRenderer();
            CacheBaseScale();
        }
    }
}
