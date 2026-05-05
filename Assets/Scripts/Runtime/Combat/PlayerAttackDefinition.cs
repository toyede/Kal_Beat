using UnityEngine;

namespace KalBeat.Combat
{
    [CreateAssetMenu(fileName = "NewPlayerAttackDefinition", menuName = "KalBeat/Combat/Player Attack Definition")]
    public sealed class PlayerAttackDefinition : ScriptableObject
    {
        [SerializeField] private string attackId = "player_attack_1";
        [SerializeField] private string displayName = "Player Attack 1";
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private string projectileFrontTipName = "FrontTip";
        [SerializeField] private float projectileTravelUnits = 1f;
        [SerializeField] private float projectileTravelDurationSec = 0.2f;

        public string AttackId
        {
            get => attackId;
            set => attackId = value;
        }

        public string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        public GameObject ProjectilePrefab
        {
            get => projectilePrefab;
            set => projectilePrefab = value;
        }

        public string ProjectileFrontTipName
        {
            get => projectileFrontTipName;
            set => projectileFrontTipName = value;
        }

        public float ProjectileTravelUnits
        {
            get => projectileTravelUnits;
            set => projectileTravelUnits = value;
        }

        public float ProjectileTravelDurationSec
        {
            get => projectileTravelDurationSec;
            set => projectileTravelDurationSec = value;
        }

        public float ResolveTravelDurationSec(float travelUnits)
        {
            float safeTravelUnits = Mathf.Max(0f, travelUnits);
            return safeTravelUnits * (projectileTravelDurationSec / projectileTravelUnits);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(projectileFrontTipName))
            {
                projectileFrontTipName = "FrontTip";
            }

            projectileTravelUnits = Mathf.Max(0.001f, projectileTravelUnits);
            projectileTravelDurationSec = Mathf.Max(0.001f, projectileTravelDurationSec);
        }
    }
}
