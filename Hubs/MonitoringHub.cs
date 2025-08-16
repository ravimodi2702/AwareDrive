using FacePOC.Models;
using FacePOC.Services;
using Microsoft.AspNetCore.SignalR;

namespace FacePOC.Hubs;

public class MonitoringHub : Hub
{
    private readonly ILogger<MonitoringHub> _logger;
    private readonly DriverMonitoringService _monitoringService;

    public MonitoringHub(
        ILogger<MonitoringHub> logger,
        DriverMonitoringService monitoringService)
    {
        _logger = logger;
        _monitoringService = monitoringService;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        
        // When a new client connects, send them the current state immediately
        var currentState = _monitoringService.CurrentState;
        
        // Create a trimmed state without the image data
        var stateToSend = new 
        {
            currentState.BlinkCount,
            currentState.SleepyCount,
            currentState.YawnCount,
            currentState.IsFaceVisible,
            currentState.IsDriverSleepy,
            currentState.IsHeadTurned,
            currentState.LastCoachingAdvice,
            currentState.RecentEvents,
            currentState.IsCalibrated,
            currentState.CalibrationMessage,
            HasFrame = currentState.CurrentFrameJpeg != null && currentState.CurrentFrameJpeg.Length > 0
        };
        
        _logger.LogInformation("Sending initial state to new client. SleepyCount={SleepyCount}", currentState.SleepyCount);
        
        await Clients.Caller.SendAsync("StateUpdated", stateToSend);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        
        if (exception != null)
        {
            _logger.LogError(exception, "Client disconnected with error");
        }
        
        await base.OnDisconnectedAsync(exception);
    }
    
    // Method that clients can call to request the current state
    public async Task RequestCurrentState()
    {
        var currentState = _monitoringService.CurrentState;
        
        // Create a trimmed state without the image data
        var stateToSend = new 
        {
            currentState.BlinkCount,
            currentState.SleepyCount,
            currentState.YawnCount,
            currentState.IsFaceVisible,
            currentState.IsDriverSleepy,
            currentState.IsHeadTurned,
            currentState.LastCoachingAdvice,
            currentState.RecentEvents,
            currentState.IsCalibrated,
            currentState.CalibrationMessage,
            HasFrame = currentState.CurrentFrameJpeg != null && currentState.CurrentFrameJpeg.Length > 0
        };
        
        _logger.LogInformation("State requested by client {ClientId}. SleepyCount={SleepyCount}", 
            Context.ConnectionId, currentState.SleepyCount);
        
        await Clients.Caller.SendAsync("StateUpdated", stateToSend);
    }
}