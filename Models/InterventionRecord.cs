using System;

namespace FacePOC.Models;

/// <summary>
/// Record of an intervention that was applied to address driver fatigue
/// </summary>
public class InterventionRecord
{
    /// <summary>
    /// Unique identifier for this intervention record
    /// </summary>
    public string InterventionId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Type of fatigue event that triggered this intervention (e.g., "Sleepy", "Yawn", "HeadTurn")
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of intervention that was applied (e.g., "Audio_Mild", "Visual_Alert")
    /// </summary>
    public string InterventionType { get; set; } = string.Empty;
    
    /// <summary>
    /// The actual message or content of the intervention
    /// </summary>
    public string InterventionContent { get; set; } = string.Empty;
    
    /// <summary>
    /// When the intervention was applied
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Severity of the fatigue event (0.0-1.0 scale)
    /// </summary>
    public double FatigueSeverity { get; set; } = 0.5;
    
    /// <summary>
    /// How long it took the driver to recover (in seconds)
    /// </summary>
    public double ResponseTime { get; set; } = 0.0;
    
    /// <summary>
    /// Whether the intervention was effective in resolving the fatigue condition
    /// </summary>
    public bool WasEffective { get; set; } = false;
    
    /// <summary>
    /// ID of the driving session this intervention occurred in
    /// </summary>
    public int SessionId { get; set; } = 0;
}