using System.Speech.Synthesis;
using FacePOC.Hubs;
using FacePOC.Models;
using Microsoft.AspNetCore.SignalR;

namespace FacePOC.Services;

/// <summary>
/// Service that manages selection and execution of interventions based on driver profiles
/// </summary>
public class InterventionManager : IDisposable
{
    private readonly ILogger<InterventionManager> _logger;
    private readonly DriverProfileStorageService _profileStorage;
    private readonly IHubContext<MonitoringHub> _hubContext;
    private readonly SpeechSynthesizer _speechSynthesizer;
    
    // Default driver ID - in a real app, this would be user-specific
    private readonly string _currentDriverId = "default"; 
    
    // List of available intervention types
    private readonly List<InterventionDefinition> _availableInterventions = new();
    
    // Track active interventions to measure response time
    private readonly Dictionary<string, DateTime> _activeInterventionStartTimes = new();
    private int _currentSessionId;
    
    public InterventionManager(
        ILogger<InterventionManager> logger,
        DriverProfileStorageService profileStorage,
        IHubContext<MonitoringHub> hubContext)
    {
        _logger = logger;
        _profileStorage = profileStorage;
        _hubContext = hubContext;
        _speechSynthesizer = new SpeechSynthesizer();
        
        // Configure speech synthesis properties for better clarity
        _speechSynthesizer.Rate = 0; // Normal rate
        _speechSynthesizer.Volume = 100; // Maximum volume
        
        // Initialize available interventions
        InitializeInterventions();
        
        // Generate new session ID
        _currentSessionId = new Random().Next(10000, 99999);
        
        _logger.LogInformation("Intervention manager initialized with session ID: {SessionId}", _currentSessionId);
    }
    
    /// <summary>
    /// Initialize the available interventions with default values
    /// </summary>
    private void InitializeInterventions()
    {
        // Audio interventions with varying urgency
        _availableInterventions.Add(new InterventionDefinition
        {
            Type = "Audio_Mild",
            Name = "Gentle voice alert",
            Messages = new List<string>
            {
                "I notice you appear to be getting sleepy. Please stay alert.",
                "You seem tired. Try to stay focused on the road.",
                "Your alertness might be decreasing. Please be careful."
            },
            EscalationLevel = 1,
            InitialEffectivenessScore = 0.5
        });
        
        _availableInterventions.Add(new InterventionDefinition
        {
            Type = "Audio_Moderate",
            Name = "Firm voice alert",
            Messages = new List<string>
            {
                "Alert! You need to pay more attention. Your eyes are closing too frequently.",
                "Warning! You're showing clear signs of fatigue. Focus on the road.",
                "Caution! You're yawning frequently. Consider taking a break soon."
            },
            EscalationLevel = 2,
            InitialEffectivenessScore = 0.6
        });
        
        _availableInterventions.Add(new InterventionDefinition
        {
            Type = "Audio_Urgent",
            Name = "Urgent voice alert",
            Messages = new List<string>
            {
                "URGENT! You appear very drowsy! Pull over safely as soon as possible!",
                "DANGER! Multiple signs of severe fatigue detected! Please stop driving!",
                "IMMEDIATE ACTION NEEDED! You are at high risk of falling asleep! Pull over now!"
            },
            EscalationLevel = 3,
            InitialEffectivenessScore = 0.7
        });
        
        // Visual interventions
        _availableInterventions.Add(new InterventionDefinition
        {
            Type = "Visual_Alert",
            Name = "Visual dashboard alert",
            Messages = new List<string>
            {
                "FATIGUE DETECTED",
                "DROWSINESS WARNING",
                "ATTENTION REQUIRED",
                "TAKE A BREAK"
            },
            EscalationLevel = 2,
            InitialEffectivenessScore = 0.5
        });
        
        // Coaching interventions
        _availableInterventions.Add(new InterventionDefinition
        {
            Type = "Coaching",
            Name = "AI coaching advice",
            Messages = new List<string>
            {
                "I've noticed several signs of fatigue. Consider opening a window for fresh air and adjusting your posture.",
                "Your alertness is decreasing. Try taking some deep breaths and consider stopping for a short walk if possible.",
                "Multiple yawns detected. This is a clear sign that you need rest. Consider finding a safe place to stop."
            },
            EscalationLevel = 2,
            InitialEffectivenessScore = 0.6
        });
        
        // No Face Detected intervention
        _availableInterventions.Add(new InterventionDefinition
        {
            Type = "NoFaceDetected",
            Name = "No face visible alert",
            Messages = new List<string>
            {
                "Your face is not visible to the camera.",
                "Please adjust your position so the camera can see your face.",
                "The system cannot detect your face. Please check your position."
            },
            EscalationLevel = 2,
            InitialEffectivenessScore = 0.6
        });
    }
    
    /// <summary>
    /// Handle a fatigue event and select the appropriate intervention
    /// </summary>
    public async Task HandleFatigueEventAsync(string eventType, double severity = 0.5, string? customMessage = null)
    {
        try
        {
            // Get the driver profile
            var profile = await _profileStorage.GetProfileAsync(_currentDriverId);
            
            // Update event counts in profile
            if (profile.FatigueEventCounts.ContainsKey(eventType))
            {
                profile.FatigueEventCounts[eventType]++;
            }
            else
            {
                profile.FatigueEventCounts[eventType] = 1;
            }
            
            // Select best intervention based on event type, severity, and past effectiveness
            var intervention = await SelectBestInterventionAsync(profile, eventType, severity, customMessage);
            
            if (intervention != null)
            {
                // Execute the intervention
                await ExecuteInterventionAsync(intervention.Value, eventType, severity);
                
                // Create record for this intervention
                var record = new InterventionRecord
                {
                    EventType = eventType,
                    InterventionType = intervention.Value.Intervention.Type,
                    InterventionContent = intervention.Value.SelectedMessage,
                    Timestamp = DateTime.Now,
                    FatigueSeverity = severity,
                    SessionId = _currentSessionId
                };
                
                // Add to history and track start time for measuring response time
                profile.InterventionHistory.Add(record);
                _activeInterventionStartTimes[record.InterventionId] = DateTime.Now;
                
                // Save the updated profile
                await _profileStorage.SaveProfileAsync(profile);
                
                _logger.LogInformation(
                    "Executed {InterventionType} for {EventType} with content: {Content}",
                    intervention.Value.Intervention.Type,
                    eventType,
                    intervention.Value.SelectedMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling fatigue event: {EventType}", eventType);
        }
    }
    
    /// <summary>
    /// Record response to intervention (e.g., when driver becomes alert again)
    /// </summary>
    public async Task RecordInterventionResponseAsync(string eventType, bool wasEffective)
    {
        try
        {
            var profile = await _profileStorage.GetProfileAsync(_currentDriverId);
            
            // Find all active interventions for this event type
            var activeInterventionIds = _activeInterventionStartTimes.Keys
                .Where(id => profile.InterventionHistory.Any(r => r.InterventionId == id && r.EventType == eventType))
                .ToList();
                
            if (activeInterventionIds.Count == 0)
            {
                _logger.LogDebug("No active interventions found for event type: {EventType}", eventType);
                return; // No active interventions to update
            }
            
            foreach (var id in activeInterventionIds)
            {
                // Find the intervention record
                var record = profile.InterventionHistory.FirstOrDefault(r => r.InterventionId == id);
                if (record == null) continue;
                
                // Calculate response time
                var startTime = _activeInterventionStartTimes[id];
                var responseTime = (DateTime.Now - startTime).TotalSeconds;
                
                // Update the record
                record.ResponseTime = responseTime;
                record.WasEffective = wasEffective;
                
                // Update effectiveness score in the profile
                UpdateEffectivenessScore(profile, record.InterventionType, wasEffective, responseTime);
                
                // Remove from active interventions
                _activeInterventionStartTimes.Remove(id);
                
                _logger.LogInformation(
                    "Recorded response for intervention {Id} of type {Type}: effective={Effective}, response time={Time}s",
                    id,
                    record.InterventionType,
                    wasEffective,
                    responseTime);
            }
            
            // Save the updated profile
            await _profileStorage.SaveProfileAsync(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording intervention response");
        }
    }
    
    /// <summary>
    /// Update the effectiveness score of an intervention type
    /// </summary>
    private void UpdateEffectivenessScore(DriverProfile profile, string interventionType, bool wasEffective, double responseTime)
    {
        if (!profile.InterventionTypeEffectiveness.TryGetValue(interventionType, out var currentScore))
        {
            // Get default initial score from intervention definition
            var intervention = _availableInterventions.FirstOrDefault(i => i.Type == interventionType);
            currentScore = intervention?.InitialEffectivenessScore ?? 0.5;
            profile.InterventionTypeEffectiveness[interventionType] = currentScore;
        }
        
        // Calculate factor based on response
        double adjustmentFactor = wasEffective ? 0.1 : -0.1;
        
        // Response time also affects score (faster is better)
        if (wasEffective)
        {
            // Faster response gives better score improvement
            if (responseTime < 2.0) adjustmentFactor += 0.05;
            else if (responseTime > 5.0) adjustmentFactor -= 0.03;
        }
        
        // Apply adjustment with limits
        double newScore = Math.Clamp(currentScore + adjustmentFactor, 0.1, 0.9);
        
        _logger.LogDebug("Updating effectiveness score for {Type}: {OldScore} -> {NewScore}",
            interventionType, currentScore, newScore);
        
        // Update the score in the profile
        profile.InterventionTypeEffectiveness[interventionType] = newScore;
    }
    
    /// <summary>
    /// Select the best intervention based on driver profile and event context
    /// </summary>
    private async Task<(InterventionDefinition Intervention, string SelectedMessage)?> SelectBestInterventionAsync(
        DriverProfile profile, string eventType, double severity, string? customMessage = null)
    {
        try
        {
            // If we have a custom message for coaching, use it directly
            if (eventType == "Coaching" && !string.IsNullOrEmpty(customMessage))
            {
                var coachingIntervention = _availableInterventions
                    .FirstOrDefault(i => i.Type == "Coaching");
                
                if (coachingIntervention != null)
                {
                    _logger.LogInformation("Using custom coaching message: {Message}", customMessage);
                    return (coachingIntervention, customMessage);
                }
            }
            
            // Handle specific event types with dedicated interventions
            if (eventType == "NoFaceDetected")
            {
                var noFaceIntervention = _availableInterventions
                    .FirstOrDefault(i => i.Type == "NoFaceDetected");
                
                if (noFaceIntervention != null)
                {
                    var random = new Random();
                    var message = noFaceIntervention.Messages[random.Next(noFaceIntervention.Messages.Count)];
                    _logger.LogInformation("Using no face detected message: {Message}", message);
                    return (noFaceIntervention, message);
                }
            }
            
            // Determine escalation level based on:
            // 1. Event frequency
            // 2. Severity of current event
            // 3. History of driver response
            
            int escalationLevel = 1; // Default is mild
            
            // Escalate based on event frequency
            int eventCount = profile.FatigueEventCounts.GetValueOrDefault(eventType, 0);
            _logger.LogDebug("Event count for {EventType}: {Count}", eventType, eventCount);
            
            if (eventCount >= 3 && eventCount < 7) 
                escalationLevel = Math.Max(escalationLevel, 2);
            else if (eventCount >= 7) 
                escalationLevel = 3;
            
            // Escalate based on current severity
            _logger.LogDebug("Event severity for {EventType}: {Severity}", eventType, severity);
            
            if (severity >= 0.6) 
                escalationLevel = Math.Max(escalationLevel, 2);
            if (severity >= 0.8) 
                escalationLevel = 3;
            
            _logger.LogInformation("Determined escalation level {Level} for {EventType} (count: {Count}, severity: {Severity})", 
                escalationLevel, eventType, eventCount, severity);
            
            // Specially handle coaching events - they should always use Coaching intervention
            if (eventType == "Coaching")
            {
                var coachingIntervention = _availableInterventions
                    .FirstOrDefault(i => i.Type == "Coaching");
                
                if (coachingIntervention != null)
                {
                    var random = new Random();
                    var message = coachingIntervention.Messages[random.Next(coachingIntervention.Messages.Count)];
                    return (coachingIntervention, message);
                }
            }
            
            // Get interventions that match the escalation level exactly
            var matchingEscalationInterventions = _availableInterventions
                .Where(i => i.EscalationLevel == escalationLevel)
                .ToList();
                
            // If no exact matches, include lower levels as fallback
            var eligibleInterventions = matchingEscalationInterventions.Any() 
                ? matchingEscalationInterventions 
                : _availableInterventions.Where(i => i.EscalationLevel <= escalationLevel).ToList();
                
            if (eligibleInterventions.Count == 0)
            {
                _logger.LogWarning("No eligible interventions found for event type {EventType}", eventType);
                return null;
            }
            
            // Debug the eligible interventions
            foreach (var intervention in eligibleInterventions)
            {
                double effectivenessScore = profile.InterventionTypeEffectiveness.GetValueOrDefault(
                    intervention.Type, intervention.InitialEffectivenessScore);
                _logger.LogDebug("Eligible intervention: {Type}, Level: {Level}, Score: {Score}", 
                    intervention.Type, intervention.EscalationLevel, effectivenessScore);
            }
            
            // Select based on effectiveness scores
            var selectedIntervention = eligibleInterventions
                .OrderByDescending(i => profile.InterventionTypeEffectiveness.GetValueOrDefault(
                    i.Type, i.InitialEffectivenessScore))
                .First();
                
            _logger.LogInformation("Selected intervention: {Type}, Level: {Level}", 
                selectedIntervention.Type, selectedIntervention.EscalationLevel);
                
            // Pick a random message from the available options
            var randomGenerator = new Random();
            var selectedMessage = selectedIntervention.Messages[randomGenerator.Next(selectedIntervention.Messages.Count)];
            
            return (selectedIntervention, selectedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error selecting intervention for {EventType}", eventType);
            return null;
        }
    }
    
    /// <summary>
    /// Execute the selected intervention
    /// </summary>
    private async Task ExecuteInterventionAsync(
        (InterventionDefinition Intervention, string SelectedMessage) intervention, 
        string eventType, 
        double severity)
    {
        var (interventionDef, message) = intervention;
        
        try
        {
            switch (interventionDef.Type)
            {
                case "Audio_Mild":
                case "Audio_Moderate":
                case "Audio_Urgent":
                    // Try speech synthesis with error handling
                    _logger.LogDebug("Executing audio intervention: {Type} - {Message}", 
                        interventionDef.Type, message);
                    _speechSynthesizer.SpeakAsync(message);
                    
                    // Also send to UI as a fallback
                    await _hubContext.Clients.All.SendAsync("FatigueAlert", new
                    {
                        Type = eventType,
                        Message = message,
                        Severity = severity,
                        AudioType = interventionDef.Type
                    });
                    break;
                    
                case "Visual_Alert":
                    // Send alert to UI
                    _logger.LogDebug("Executing visual intervention: {Type} - {Message}", 
                        interventionDef.Type, message);
                    await _hubContext.Clients.All.SendAsync("FatigueAlert", new
                    {
                        Type = eventType,
                        Message = message,
                        Severity = severity
                    });
                    break;
                    
                case "Coaching":
                    // Send coaching advice through both channels
                    _logger.LogDebug("Executing coaching intervention: {Message}", message);
                    _speechSynthesizer.SpeakAsync(message);
                    await _hubContext.Clients.All.SendAsync("CoachingReceived", message);
                    break;
                    
                case "NoFaceDetected":
                    // Handle no face detected with specific message
                    _logger.LogDebug("Executing no face detected intervention: {Message}", message);
                    _speechSynthesizer.SpeakAsync(message);
                    await _hubContext.Clients.All.SendAsync("FatigueAlert", new
                    {
                        Type = eventType,
                        Message = message,
                        Severity = severity,
                        AudioType = "Audio_Moderate" // Use moderate level for UI styling
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing intervention: {Type}", interventionDef.Type);
            
            // Fallback to visual notification if speech fails
            try
            {
                await _hubContext.Clients.All.SendAsync("FatigueAlert", new
                {
                    Type = eventType,
                    Message = $"[{interventionDef.Type}] {message}",
                    Severity = severity,
                    IsFallback = true
                });
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to send fallback alert");
            }
        }
    }
    
    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        try
        {
            _speechSynthesizer.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing speech synthesizer");
        }
    }
}