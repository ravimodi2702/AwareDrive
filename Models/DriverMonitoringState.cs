namespace FacePOC.Models;

public class DriverMonitoringState
{
    public int BlinkCount { get; set; }
    public int SleepyCount { get; set; }  // Count of sleepiness detections
    public int YawnCount { get; set; }
    public bool IsFaceVisible { get; set; } = true;
    public bool IsDriverSleepy { get; set; }
    public bool IsHeadTurned { get; set; }
    public string? LastCoachingAdvice { get; set; }
    public List<string> RecentEvents { get; set; } = new List<string>();
    public bool IsCalibrated { get; set; }
    public string? CalibrationMessage { get; set; }
    public byte[]? CurrentFrameJpeg { get; set; }
}

public class FaceDetectionEvent
{
    public string EventType { get; set; } = string.Empty; // "Blink", "Yawn", "HeadTurn", "Sleepy", "NoFaceDetected"
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Description { get; set; } = string.Empty;
}