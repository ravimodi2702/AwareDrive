using FacePOC.Models;
using FacePOC.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacePOC.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriverMonitoringController : ControllerBase
{
    private readonly ILogger<DriverMonitoringController> _logger;
    private readonly DriverMonitoringService _monitoringService;
    private readonly DriverProfileStorageService _profileStorage;

    public DriverMonitoringController(
        ILogger<DriverMonitoringController> logger,
        DriverMonitoringService monitoringService,
        DriverProfileStorageService profileStorage)
    {
        _logger = logger;
        _monitoringService = monitoringService;
        _profileStorage = profileStorage;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartMonitoring()
    {
        try
        {
            await _monitoringService.StartMonitoringAsync();
            return Ok(new { success = true, message = "Monitoring started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting monitoring");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("stop")]
    public async Task<IActionResult> StopMonitoring()
    {
        try
        {
            await _monitoringService.StopMonitoringAsync();
            return Ok(new { success = true, message = "Monitoring stopped" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping monitoring");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("state")]
    public IActionResult GetCurrentState()
    {
        try
        {
            var state = _monitoringService.CurrentState;
            
            // Create a copy of the state without the image to keep the response small
            var stateWithoutImage = new
            {
                state.BlinkCount,
                state.SleepyCount,  // Added SleepyCount
                state.YawnCount,
                state.IsFaceVisible,
                state.IsDriverSleepy,
                state.IsHeadTurned,
                state.LastCoachingAdvice,
                state.RecentEvents,
                state.IsCalibrated,
                state.CalibrationMessage,
                HasFrame = state.CurrentFrameJpeg != null && state.CurrentFrameJpeg.Length > 0
            };
            
            return Ok(stateWithoutImage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current state");
            return StatusCode(500, new { error = "Failed to get current state", message = ex.Message });
        }
    }

    [HttpGet("frame")]
    public IActionResult GetCurrentFrame()
    {
        try
        {
            var frame = _monitoringService.CurrentState.CurrentFrameJpeg;
            if (frame == null || frame.Length == 0)
            {
                return NotFound(new { message = "No frame available" });
            }
            
            return File(frame, "image/jpeg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current frame");
            return StatusCode(500, new { error = "Failed to get current frame", message = ex.Message });
        }
    }
    
    [HttpPost("reset-interventions")]
    public async Task<IActionResult> ResetInterventions(string driverId = "default")
    {
        try
        {
            await _profileStorage.ResetInterventionEffectivenessAsync(driverId);
            return Ok(new { success = true, message = $"Intervention effectiveness scores reset for driver {driverId}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting intervention effectiveness scores");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}