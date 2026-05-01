using UnityEngine;

namespace KalBeat.Combat
{
    public sealed class RangedMagicCircleView : MonoBehaviour
    {
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private bool faceCamera = true;

        private int segmentCount = 48;
        private float radius = 0.55f;

        public void Initialize(Material lineMaterial, float circleRadius, float lineWidth, int circleSegmentCount)
        {
            radius = Mathf.Max(0.01f, circleRadius);
            segmentCount = Mathf.Max(12, circleSegmentCount);
            EnsureLineRenderer();

            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = segmentCount;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;
            lineRenderer.material = lineMaterial;

            BuildCircle();
        }

        public void SetVisualState(int remainingShots, Color baseColor, Color stackedColor, float stackedScale)
        {
            EnsureLineRenderer();

            Color targetColor = remainingShots > 1 ? stackedColor : baseColor;
            lineRenderer.startColor = targetColor;
            lineRenderer.endColor = targetColor;

            float targetScale = remainingShots > 1 ? Mathf.Max(1f, stackedScale) : 1f;
            transform.localScale = Vector3.one * targetScale;
        }

        public void Hide()
        {
            if (lineRenderer != null)
            {
                lineRenderer.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (!faceCamera)
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

        private void EnsureLineRenderer()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }
        }

        private void BuildCircle()
        {
            // 텍스처가 생기기 전까지 LineRenderer 원형으로 마법진을 대신 표현한다.
            for (int i = 0; i < segmentCount; i++)
            {
                float angle = i * Mathf.PI * 2f / segmentCount;
                Vector3 point = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                lineRenderer.SetPosition(i, point);
            }
        }

        private void OnValidate()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }
        }
    }
}
