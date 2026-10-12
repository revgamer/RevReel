namespace RevReel.Models
{
    /// <summary>What part of the screen(s) a recording captures.</summary>
    public enum RecordingSourceKind
    {
        /// <summary>One monitor (the main one unless another is chosen).</summary>
        Monitor,

        /// <summary>Every monitor together, as one wide video.</summary>
        AllMonitors,

        /// <summary>A rectangle the user dragged out on one monitor.</summary>
        Area
    }
}
