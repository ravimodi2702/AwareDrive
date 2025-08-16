using System.Collections.Generic;

namespace FacePOC.Models;

/// <summary>
/// Definition of an available intervention type
/// </summary>
public class InterventionDefinition
{
    /// <summary>
    /// Type identifier for this intervention (Audio_Mild, Visual_Alert, etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Human-readable name of this intervention
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Available messages/content for this intervention
    /// </summary>
    public List<string> Messages { get; set; } = new();
    
    /// <summary>
    /// Starting effectiveness score for new drivers
    /// </summary>
    public double InitialEffectivenessScore { get; set; } = 0.5;
    
    /// <summary>
    /// Escalation level (1=mild, 5=severe)
    /// </summary>
    public int EscalationLevel { get; set; } = 1;
    
    /// <summary>
    /// Whether this intervention requires driver acknowledgment
    /// </summary>
    public bool RequiresAcknowledgment { get; set; } = false;
}