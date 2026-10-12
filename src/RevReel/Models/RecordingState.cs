namespace RevReel.Models
{
    /// <summary>What the recorder is doing right now.</summary>
    public enum RecordingState
    {
        /// <summary>Not recording. Ready to start.</summary>
        Idle,

        /// <summary>A recording is running.</summary>
        Recording,

        /// <summary>Stop was requested. Waiting for the file to be finished and closed.</summary>
        Stopping
    }
}
