using System;
using System.Collections.Generic;

namespace FacePOC.Models;

/// <summary>
/// Represents a driver's profile with their intervention history and preferences
/// </summary>
public class DriverProfile
{
    /// <summary>
    /// Unique identifier for the driver
    /// </summary>
    public string DriverId { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Driver's name
    /// </summary>
    public string Name { get; set; } = "Default Driver";
    
    /// <summary>
    /// When the driver was first seen
    /// </summary>
    public DateTime FirstSeen { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Last time driver was active
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Effectiveness scores for different intervention types
    /// </summary>
    public Dictionary<string, double> InterventionTypeEffectiveness { get; set; } = new();
    
    /// <summary>
    /// History of interventions and their outcomes
    /// </summary>
    public List<InterventionRecord> InterventionHistory { get; set; } = new();
    
    /// <summary>
    /// Average time it takes for this driver to recover from fatigue events
    /// </summary>
    public double AverageRecoveryTime { get; set; } = 3.0; // in seconds
    
    /// <summary>
    /// Number of driving sessions for this driver
    /// </summary>
    public int TotalDrivingSessionCount { get; set; } = 0;
    
    /// <summary>
    /// Count of different fatigue events for this driver
    /// </summary>
    public Dictionary<string, int> FatigueEventCounts { get; set; } = new()
    {
        { "Sleepy", 0 },
        { "Yawn", 0 },
        { "HeadTurn", 0 },
        { "NoFaceDetected", 0 }
    };
}