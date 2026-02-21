using FFII_ScreenReader.Field;

namespace FFII_ScreenReader.Core.Filters
{
    /// <summary>
    /// Filters out ToLayer (layer transition) entities.
    /// When enabled, hides layer transitions from the navigation list.
    /// Default: disabled (ToLayer entities shown).
    /// </summary>
    public class ToLayerFilter : IEntityFilter
    {
        private bool isEnabled = false;

        public bool IsEnabled
        {
            get => isEnabled;
            set => isEnabled = value;
        }

        public string Name => "Layer Transition Filter";

        public FilterTiming Timing => FilterTiming.OnAdd;

        public bool PassesFilter(NavigableEntity entity, FilterContext context)
        {
            // When enabled, reject EventEntity instances with ToLayer type
            if (entity is EventEntity eventEntity &&
                eventEntity.EventTypeName == "ToLayer")
            {
                return false;
            }

            return true;
        }

        public void OnEnabled() { }
        public void OnDisabled() { }
    }
}
