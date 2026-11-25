namespace ReHand.Core
{
    /// <summary>Input modality used by the participant.</summary>
    public enum InputMode
    {
        None = 0,
        Hands = 1,
        Controllers = 2,
    }

    /// <summary>Arm(s) trained in the current session.</summary>
    public enum TrainedArm
    {
        Left = 0,
        Right = 1,
        Both = 2,
    }

    /// <summary>Hand-tracking quality for one hand, after debouncing.</summary>
    public enum HandTrackingState
    {
        Tracked = 0,
        LowConfidence = 1,
        Lost = 2,
    }
}
