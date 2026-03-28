using System;
using UnityEngine;

namespace KalBeat.Charting
{
    [Serializable]
    public sealed class LaneBindingData
    {
        [SerializeField] private LaneBindingType bindingType = LaneBindingType.None;
        [SerializeField] private string bindingId = string.Empty;

        public LaneBindingType BindingType
        {
            get => bindingType;
            set => bindingType = value;
        }

        public string BindingId
        {
            get => bindingId;
            set => bindingId = value;
        }
    }
}
