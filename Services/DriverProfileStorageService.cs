using System.Text.Json;
using FacePOC.Models;

namespace FacePOC.Services;

/// <summary>
/// Service that handles storing and retrieving driver profiles using JSON files
/// </summary>
public class DriverProfileStorageService
{
    private readonly string _profileDirectory;
    private readonly ILogger<DriverProfileStorageService> _logger;
    private readonly Dictionary<string, DriverProfile> _profileCache = new();
    
    public DriverProfileStorageService(ILogger<DriverProfileStorageService> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        // Use app data folder or configuration-specified location
        string baseDirectory = configuration["InterventionSystem:ProfileDirectory"] ?? 
                              Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                                           "DriverMonitoring");
                              
        _profileDirectory = Path.Combine(baseDirectory, "Profiles");
        Directory.CreateDirectory(_profileDirectory); // Ensure directory exists
        
        _logger.LogInformation("Profile storage initialized at {Path}", _profileDirectory);
    }
    
    /// <summary>
    /// Get a driver profile by ID, creating a new one if it doesn't exist
    /// </summary>
    public async Task<DriverProfile> GetProfileAsync(string driverId)
    {
        // Check cache first
        if (_profileCache.TryGetValue(driverId, out var cachedProfile))
        {
            return cachedProfile;
        }
        
        // Otherwise read from file
        string profilePath = Path.Combine(_profileDirectory, $"{driverId}.json");
        if (!File.Exists(profilePath))
        {
            _logger.LogInformation("Creating new driver profile for ID: {DriverId}", driverId);
            return CreateNewProfile(driverId);
        }
        
        try
        {
            string json = await File.ReadAllTextAsync(profilePath);
            var profile = JsonSerializer.Deserialize<DriverProfile>(json);
            
            if (profile != null)
            {
                _profileCache[driverId] = profile;
                return profile;
            }
            
            _logger.LogWarning("Failed to deserialize profile for driver {DriverId}, creating new one", driverId);
            return CreateNewProfile(driverId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading driver profile for {DriverId}", driverId);
            return CreateNewProfile(driverId);
        }
    }
    
    /// <summary>
    /// Save a driver profile to disk
    /// </summary>
    public async Task SaveProfileAsync(DriverProfile profile)
    {
        string profilePath = Path.Combine(_profileDirectory, $"{profile.DriverId}.json");
        
        try
        {
            // Update cache
            _profileCache[profile.DriverId] = profile;
            
            // Update last seen timestamp
            profile.LastSeen = DateTime.Now;
            
            // Save to file
            string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(profilePath, json);
            
            _logger.LogDebug("Saved profile for driver {DriverId}", profile.DriverId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving driver profile for {DriverId}", profile.DriverId);
        }
    }
    
    /// <summary>
    /// Reset the intervention effectiveness scores for a driver
    /// </summary>
    public async Task ResetInterventionEffectivenessAsync(string driverId)
    {
        try
        {
            var profile = await GetProfileAsync(driverId);
            
            // Reset to balanced initial values
            profile.InterventionTypeEffectiveness["Audio_Mild"] = 0.5;
            profile.InterventionTypeEffectiveness["Audio_Moderate"] = 0.6;
            profile.InterventionTypeEffectiveness["Audio_Urgent"] = 0.7;
            profile.InterventionTypeEffectiveness["Visual_Alert"] = 0.5;
            profile.InterventionTypeEffectiveness["Coaching"] = 0.6;
            
            await SaveProfileAsync(profile);
            
            _logger.LogInformation("Reset intervention effectiveness scores for driver {DriverId}", driverId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting intervention effectiveness for {DriverId}", driverId);
        }
    }
    
    /// <summary>
    /// Create a new driver profile with default values
    /// </summary>
    private DriverProfile CreateNewProfile(string driverId)
    {
        var profile = new DriverProfile { DriverId = driverId };
        
        // Initialize with default intervention effectiveness values
        profile.InterventionTypeEffectiveness["Audio_Mild"] = 0.5;
        profile.InterventionTypeEffectiveness["Audio_Moderate"] = 0.6;
        profile.InterventionTypeEffectiveness["Audio_Urgent"] = 0.7;
        profile.InterventionTypeEffectiveness["Visual_Alert"] = 0.5;
        profile.InterventionTypeEffectiveness["Coaching"] = 0.6;
        
        _profileCache[driverId] = profile;
        
        // Save the new profile to disk
        string profilePath = Path.Combine(_profileDirectory, $"{driverId}.json");
        string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllTextAsync(profilePath, json).ConfigureAwait(false);
        
        return profile;
    }
    
    /// <summary>
    /// Delete a profile file
    /// </summary>
    public void DeleteProfile(string driverId)
    {
        string profilePath = Path.Combine(_profileDirectory, $"{driverId}.json");
        
        try
        {
            if (File.Exists(profilePath))
            {
                File.Delete(profilePath);
                _profileCache.Remove(driverId);
                _logger.LogInformation("Deleted profile for driver {DriverId}", driverId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting profile for {DriverId}", driverId);
        }
    }
}