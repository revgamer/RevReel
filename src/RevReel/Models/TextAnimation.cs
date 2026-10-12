namespace RevReel.Models
{
    /// <summary>How a text moves or changes in the GIF. The order is the order of the list in the text window.</summary>
    public enum TextAnimation
    {
        None = 0,
        Flash = 1,       // blinks on and off
        Fade = 2,        // fades in and out
        Shake = 3,       // the whole text shakes
        Broken = 4,      // broken, glitching letters
        Typewriter = 5,  // letters appear one by one
        Rainbow = 6,     // colours cycle
    }
}
