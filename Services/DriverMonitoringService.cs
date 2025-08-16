using System.Collections.Concurrent;
using System.Speech.Synthesis;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.Vision.Face;
using FacePOC.Hubs;
using FacePOC.Models;
using Microsoft.AspNetCore.SignalR;
using OpenCvSharp;

namespace FacePOC.Services;

/// <summary>
/// Service responsible for monitoring driver fatigue indicators using computer vision and AI.
/// </summary>
public class DriverMonitoringService : IDisposable
{
    #region Constants

    private const double DEFAULT_SLEEPY_THRESHOLD_SECONDS = 1.5;
    private const double EYE_BLINK_THRESHOLD_RATIO = 0.7;
    private const double MOUTH_YAWN_THRESHOLD_RATIO = 0.11;
    private const double YAWN_HOLD_DURATION_SECONDS = 1.5;
    private const double HEAD_TURN_THRESHOLD_DEGREES = 20.0;
    private const double HEAD_TURN_DURATION_THRESHOLD_SECONDS = 5.0;
    private const double NO_FACE_THRESHOLD_SECONDS = 15.0;
    private const double SLEEPY_EVENT_DEBOUNCE_SECONDS = 3.0;
    private const int EAR_BUFFER_SIZE = 5;
    private const int CALIBRATION_SAMPLES = 5;
    private const double EAR_BASELINE_ADAPTATION_RATE = 0.01;
    private const int EVENT_DISPLAY_COOLDOWN_SECONDS = 10;
    private const int COACHING_ADVISORY_INTERVAL_MS = 60000; // 1 minute
    private const int FRAME_INTERVAL_MS = 33; // ~30fps
    private const int FACE_DETECTION_INTERVAL_MS = 100;
    private const int FACE_DETECTION_FREQUENCY_SECONDS = 1;
    private const int NO_FACE_ALERT_COOLDOWN_SECONDS = 10; // Cooldown for face detection alerts

    #endregion

    #region Fields

    private readonly ILogger<DriverMonitoringService> _logger;
    private readonly IHubContext<MonitoringHub> _hubContext;
    private readonly FaceClient _faceClient;
    private readonly OpenAIClient _openAIClient;
    private readonly string _deploymentName;
    private readonly SpeechSynthesizer _speechSynthesizer;
    private readonly InterventionManager _interventionManager;

    // Camera capture related fields
    private VideoCapture? _capture;
    private Mat? _frame;
    private List<FaceDetectionResult> _faceBuffer = new();
    private readonly object _faceLock = new();

    // Driver monitoring state tracking
    private readonly DriverMetrics _metrics = new();
    private readonly EventTracker _eventTracker = new();
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;
    private DateTime _lastNoFaceAlertTime = DateTime.MinValue; // Track when we last alerted about no face

    #endregion

    #region Properties

    /// <summary>
    /// Gets the current monitoring state that can be accessed externally.
    /// </summary>
    public DriverMonitoringState CurrentState { get; } = new();

    #endregion

    #region Constructor

    public DriverMonitoringService(
        ILogger<DriverMonitoringService> logger,
        IHubContext<MonitoringHub> hubContext,
        IConfiguration configuration,
        InterventionManager interventionManager)
    {
        _logger = logger;
        _hubContext = hubContext;
        _speechSynthesizer = new SpeechSynthesizer();
        _interventionManager = interventionManager;
        
        // Configure Azure Face API
        string endpoint = configuration["Azure:FaceApi:Endpoint"] ?? 
                          "https://face-api-hackathon.cognitiveservices.azure.com/";
        string apiKey = configuration["Azure:FaceApi:ApiKey"];
        _faceClient = new FaceClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

        // Configure Azure OpenAI - Parse just the base URL (without query parameters)
        string openAiEndpointConfig = configuration["Azure:OpenAI:Endpoint"];
                         
        // Extract base URL from endpoint (remove any query parameters)
        Uri openAiUri = new Uri(openAiEndpointConfig);
        string baseEndpoint = $"{openAiUri.Scheme}://{openAiUri.Host}/";
        
        Uri azureEndpoint = new(baseEndpoint);
        string azureKey = configuration["Azure:OpenAI:ApiKey"];
        _deploymentName = configuration["Azure:OpenAI:DeploymentName"] ?? "gpt-4.1";
        
        _openAIClient = new OpenAIClient(azureEndpoint, new AzureKeyCredential(azureKey));

        _logger.LogInformation("DriverMonitoringService initialized with Face API endpoint: {Endpoint}", endpoint);
        _logger.LogInformation("Azure OpenAI configured with endpoint: {Endpoint} and deployment: {Deployment}", 
            baseEndpoint, _deploymentName);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts the driver monitoring process.
    /// </summary>
    public async Task StartMonitoringAsync()
    {
        if (_isRunning)
        {
            _logger.LogWarning("Monitoring is already running");
            return;
        }
        
        _cancellationTokenSource = new CancellationTokenSource();
        _isRunning = true;
        
        try
        {
            _capture = new VideoCapture(0);
            _frame = new Mat();
            
            // Reset all tracking state
            ResetState();
            
            // Start background monitoring tasks
            _ = Task.Run(async () => await RunFaceDetectionLoopAsync(_cancellationTokenSource.Token));
            _ = Task.Run(async () => await RunGptAdvisoryLoopAsync(_cancellationTokenSource.Token));
            _ = Task.Run(async () => await RunMainCaptureLoopAsync(_cancellationTokenSource.Token));
            
            _logger.LogInformation("Driver monitoring started successfully");
            await _hubContext.Clients.All.SendAsync("MonitoringStarted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting driver monitoring");
            _isRunning = false;
            throw;
        }
    }

    /// <summary>
    /// Stops the driver monitoring process.
    /// </summary>
    public async Task StopMonitoringAsync()
    {
        if (!_isRunning)
        {
            return;
        }
        
        _cancellationTokenSource?.Cancel();
        _isRunning = false;
        
        await DisposeResourcesAsync();
        
        _logger.LogInformation("Driver monitoring stopped");
        await _hubContext.Clients.All.SendAsync("MonitoringStopped");
    }

    #endregion

    #region Private Methods - Main Monitoring Loops

    /// <summary>
    /// Main video capture and processing loop.
    /// </summary>
    private async Task RunMainCaptureLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting main capture loop");
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Skip if camera not initialized
                if (_capture == null || _frame == null)
                {
                    await Task.Delay(FRAME_INTERVAL_MS, cancellationToken);
                    continue;
                }
                
                // Read the next frame
                _capture.Read(_frame);
                
                // Skip if frame is empty
                if (_frame.Empty())
                {
                    _logger.LogWarning("Empty frame captured");
                    await Task.Delay(FACE_DETECTION_INTERVAL_MS, cancellationToken);
                    continue;
                }
                
                // Resize frame to standard size
                Cv2.Resize(_frame, _frame, new OpenCvSharp.Size(640, 480));
                
                // Get detected faces from buffer
                List<FaceDetectionResult> faces;
                lock (_faceLock)
                {
                    faces = _faceBuffer.ToList();
                    _faceBuffer.Clear();
                }
                
                // Find nearest face (largest face rectangle, which is closest to camera)
                FaceDetectionResult? nearestFace = null;
                if (faces.Count > 0)
                {
                    // Get the largest face (closest to camera)
                    nearestFace = GetNearestFace(faces);
                    
                    if (faces.Count > 1)
                    {
                        _logger.LogDebug("Multiple faces detected ({Count}), processing the nearest one", faces.Count);
                    }
                }
                
                // Process the nearest face for fatigue indicators
                if (nearestFace != null)
                {
                    ProcessFacialFeatures(nearestFace, _frame);
                }
                
                // Prepare frame for UI display
                byte[] imageBytes;
                Cv2.ImEncode(".jpg", _frame, out imageBytes);
                CurrentState.CurrentFrameJpeg = imageBytes;
                
                // Update and synchronize state
                await SynchronizeStateAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in main capture loop");
            }
            
            await Task.Delay(FRAME_INTERVAL_MS, cancellationToken);
        }
    }

    /// <summary>
    /// Gets the nearest face to the camera (largest face rectangle)
    /// </summary>
    private FaceDetectionResult GetNearestFace(List<FaceDetectionResult> faces)
    {
        if (faces.Count == 1)
            return faces[0];
            
        return faces
            .OrderByDescending(face => {
                var rect = face.FaceRectangle;
                return rect != null ? rect.Width * rect.Height : 0;
            })
            .First();
    }

    /// <summary>
    /// Face detection loop using Azure Face API.
    /// </summary>
    private async Task RunFaceDetectionLoopAsync(CancellationToken cancellationToken)
    {
        DateTime lastDetectionTime = DateTime.MinValue;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_frame == null || _frame.Empty())
                {
                    _logger.LogDebug("Waiting for frame...");
                    await Task.Delay(FACE_DETECTION_INTERVAL_MS, cancellationToken);
                    continue;
                }

                if ((DateTime.Now - lastDetectionTime).TotalSeconds > FACE_DETECTION_FREQUENCY_SECONDS)
                {
                    lastDetectionTime = DateTime.Now;
                    
                    // Create a copy of the frame for processing
                    using Mat frameCopy = _frame.Clone();
                    
                    // Convert to JPEG for API
                    byte[] imageBytes;
                    Cv2.ImEncode(".jpg", frameCopy, out imageBytes);
                    using var stream = new MemoryStream(imageBytes);
                    
                    // Call Face API to detect faces
                    var detectResponse = await _faceClient.DetectAsync(
                        BinaryData.FromStream(stream),
                        FaceDetectionModel.Detection03,
                        FaceRecognitionModel.Recognition04,
                        returnFaceId: false,
                        returnFaceAttributes: new FaceAttributeType[] { FaceAttributeType.HeadPose },
                        returnFaceLandmarks: true,
                        returnRecognitionModel: false,
                        cancellationToken: cancellationToken
                    );
                    
                    // Handle no face detected case
                    if (detectResponse.Value.Count == 0)
                    {
                        await HandleNoFaceDetectedAsync(cancellationToken);
                    }
                    else
                    {
                        // Handle face detected case
                        HandleFaceDetected();
                        
                        // Store detected faces for processing
                        lock (_faceLock)
                        {
                            // Clear and add instead of direct assignment
                            _faceBuffer.Clear();
                            _faceBuffer.AddRange(detectResponse.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in face detection");
            }
            
            await Task.Delay(FACE_DETECTION_INTERVAL_MS, cancellationToken);
        }
    }

    /// <summary>
    /// AI coaching advice loop using Azure OpenAI.
    /// </summary>
    private async Task RunGptAdvisoryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(COACHING_ADVISORY_INTERVAL_MS, cancellationToken);
                
                string summary = _eventTracker.GenerateSummary();
                _logger.LogInformation("[Summary] {Summary}", summary);
                
                if (string.IsNullOrEmpty(summary))
                {
                    _logger.LogInformation("No significant events in the last minute");
                    continue;
                }
                
                string coaching = await GetCoachingAdviceAsync(summary, cancellationToken);
                _logger.LogInformation("[Coaching] {Coaching}", coaching);
                
                if (!string.IsNullOrEmpty(coaching))
                {
                    // Use intervention manager for coaching with the custom message
                    await _interventionManager.HandleFatigueEventAsync("Coaching", 0.6, coaching);
                    
                    CurrentState.LastCoachingAdvice = coaching;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GPT advisory loop");
            }
        }
    }

    #endregion

    #region Private Methods - Face Processing

    /// <summary>
    /// Main entry point for processing facial features for fatigue detection.
    /// </summary>
    private void ProcessFacialFeatures(FaceDetectionResult face, Mat frame)
    {
        // Process eye metrics and blink detection
        ProcessEyeMetrics(face, frame);
        
        // Process mouth/yawn detection
        ProcessYawnDetection(face, frame);
        
        // Process head position
        ProcessHeadPosition(face);
    }

    /// <summary>
    /// Process eye metrics for blink detection and drowsiness.
    /// </summary>
    private void ProcessEyeMetrics(FaceDetectionResult face, Mat frame)
    {
        var landmarks = face.FaceLandmarks;
        if (landmarks == null) return;
        
        // Draw visual indicators
        DrawEyeBoxes(landmarks, frame);
        
        // Calculate Eye Aspect Ratio (EAR)
        double leftEAR = ComputeEAR(landmarks, isLeftEye: true);
        double rightEAR = ComputeEAR(landmarks, isLeftEye: false);
        double avgEAR = (leftEAR + rightEAR) / 2.0;
        
        // Add to buffer for smoothing
        _metrics.EarBuffer.Enqueue(avgEAR);
        while (_metrics.EarBuffer.Count > EAR_BUFFER_SIZE) 
            _metrics.EarBuffer.TryDequeue(out _);
        
        double smoothedEAR = _metrics.EarBuffer.Average();
        
        // Handle calibration
        if (!_metrics.IsCalibrated)
        {
            if (_metrics.EarBuffer.Count == CALIBRATION_SAMPLES)
            {
                _metrics.BaselineEAR = smoothedEAR;
                _metrics.IsCalibrated = true;
                string calibrationMsg = $"Calibrated EAR: {_metrics.BaselineEAR:F3}";
                _logger.LogInformation(calibrationMsg);
                CurrentState.IsCalibrated = true;
                CurrentState.CalibrationMessage = calibrationMsg;
            }
            return;
        }
        
        // Adapt baseline EAR gradually
        _metrics.BaselineEAR = _metrics.BaselineEAR * (1 - EAR_BASELINE_ADAPTATION_RATE) + 
                               smoothedEAR * EAR_BASELINE_ADAPTATION_RATE;
        
        // Calculate blink threshold based on baseline
        double blinkThreshold = _metrics.BaselineEAR * EYE_BLINK_THRESHOLD_RATIO;
        
        if (smoothedEAR < blinkThreshold)
        {
            HandleEyesClosed();
        }
        else
        {
            HandleEyesOpened();
        }
    }

    /// <summary>
    /// Handles logic when eyes are detected as closed.
    /// </summary>
    private void HandleEyesClosed()
    {
        if (!_metrics.AreEyesClosed)
        {
            _metrics.AreEyesClosed = true;
            _metrics.EyeClosureStartTime = DateTime.Now;
            _logger.LogDebug("Eyes closed detected at {time}", DateTime.Now);
        }
        
        TimeSpan closedDuration = DateTime.Now - _metrics.EyeClosureStartTime;
        
        // Check for sleepiness (eyes closed longer than threshold)
        if (closedDuration.TotalSeconds >= DEFAULT_SLEEPY_THRESHOLD_SECONDS)
        {
            // Debounce detection to avoid multiple counts
            if ((DateTime.Now - _metrics.LastSleepyDetectionTime).TotalSeconds >= SLEEPY_EVENT_DEBOUNCE_SECONDS)
            {
                DetectSleepyEvent();
            }
        }
    }

    /// <summary>
    /// Records a sleepy event when driver's eyes remain closed too long.
    /// </summary>
    private void DetectSleepyEvent()
    {
        _metrics.SleepyCount++;
        _metrics.LastSleepyDetectionTime = DateTime.Now;
        
        var closedDuration = (DateTime.Now - _metrics.EyeClosureStartTime).TotalSeconds;
        string sleepyMsg = $"[ALERT] Driver looks sleepy! Eyes closed for {closedDuration:F1}s - Total count: {_metrics.SleepyCount}";
        _logger.LogWarning(sleepyMsg);
        
        // Log event
        string eventTimestamp = DateTime.Now.ToString("HH:mm:ss");
        _eventTracker.LogEvent($"Sleepy at {eventTimestamp}");
        
        // Update state
        CurrentState.IsDriverSleepy = true;
        CurrentState.SleepyCount = _metrics.SleepyCount;
        
        _logger.LogInformation("Sleepy count updated to {count}", _metrics.SleepyCount);
        
        // Notify UI
        _hubContext.Clients.All.SendAsync("DriverSleepy").Wait();
        
        // Add to recent events if not duplicated
        AddEventIfNotRecent("Sleepy", eventTimestamp);
        
        // Calculate severity based on duration and frequency
        double severity = Math.Min(closedDuration / 5.0, 1.0); // 5+ seconds = max severity
        severity = Math.Min(severity + (_metrics.SleepyCount / 10.0), 1.0); // More events increase severity
        
        // Use intervention manager for adaptive intervention selection
        _interventionManager.HandleFatigueEventAsync("Sleepy", severity).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles logic when eyes are detected as open.
    /// </summary>
    private void HandleEyesOpened()
    {
        if (_metrics.AreEyesClosed)
        {
            TimeSpan closedDuration = DateTime.Now - _metrics.EyeClosureStartTime;
            
            // If eyes were closed but not long enough for sleepiness, count as blink
            if (closedDuration.TotalSeconds < DEFAULT_SLEEPY_THRESHOLD_SECONDS)
            {
                _metrics.BlinkCount++;
                _logger.LogDebug("Blink detected at {time}", DateTime.Now);
            }
            else
            {
                _logger.LogDebug("Eyes opened after {duration} seconds", closedDuration.TotalSeconds);
                
                // Record response to sleepy interventions as effective
                _interventionManager.RecordInterventionResponseAsync("Sleepy", true).ConfigureAwait(false);
            }
            
            _metrics.AreEyesClosed = false;
            CurrentState.IsDriverSleepy = false;
        }
    }

    /// <summary>
    /// Process mouth features for yawn detection.
    /// </summary>
    private void ProcessYawnDetection(FaceDetectionResult face, Mat frame)
    {
        var landmarks = face.FaceLandmarks;
        var faceRect = face.FaceRectangle;
        
        if (landmarks == null || faceRect == null) 
            return;
        
        // Draw visual indicators
        DrawMouthBox(landmarks, frame);
        
        // Calculate mouth openness ratio
        double mouthOpen = (landmarks.UnderLipBottom.Y - landmarks.UpperLipTop.Y) / faceRect.Height;
        
        if (mouthOpen > MOUTH_YAWN_THRESHOLD_RATIO)
        {
            HandleMouthOpen(mouthOpen);
        }
        else
        {
            if (_metrics.IsMouthHeldOpen && _metrics.IsYawnInProgress)
            {
                _logger.LogInformation($"mouthopen: {mouthOpen}");
                // Record response to yawn interventions as effective when mouth closes
                _interventionManager.RecordInterventionResponseAsync("Yawn", true).ConfigureAwait(false);
            }
            
            _metrics.IsMouthHeldOpen = false;
            _metrics.IsYawnInProgress = false;
        }
    }

    /// <summary>
    /// Handles logic when mouth is detected as open.
    /// </summary>
    private void HandleMouthOpen(double mouthOpenRatio)
    {
        if (!_metrics.IsMouthHeldOpen)
        {
            _metrics.IsMouthHeldOpen = true;
            _metrics.MouthOpenStartTime = DateTime.Now;
        }
        else
        {
            TimeSpan openDuration = DateTime.Now - _metrics.MouthOpenStartTime;
            
            // Check if mouth has been open long enough to be a yawn
            if (openDuration.TotalSeconds >= YAWN_HOLD_DURATION_SECONDS && !_metrics.IsYawnInProgress)
            {
                DetectYawnEvent();
            }
        }
    }

    /// <summary>
    /// Records a yawn event.
    /// </summary>
    private void DetectYawnEvent()
    {
        _metrics.IsYawnInProgress = true;
        _metrics.YawnCount++;
        
        string eventTimestamp = DateTime.Now.ToString("HH:mm:ss");
        string yawnMsg = $"Yawn detected at {eventTimestamp}";
        _logger.LogInformation(yawnMsg);
        
        // Log event
        _eventTracker.LogEvent($"Yawn at {eventTimestamp}");
        
        // Add to recent events if not duplicated
        AddEventIfNotRecent("Yawn", eventTimestamp);
        
        // Notify UI
        _hubContext.Clients.All.SendAsync("YawnDetected").Wait();
        
        // Calculate severity based on yawn count and frequency
        double severity = Math.Min(0.4 + (_metrics.YawnCount * 0.1), 0.9);
        
        // Use intervention manager for adaptive intervention selection
        if (_metrics.YawnCount >= 3)
        {
            _interventionManager.HandleFatigueEventAsync("Yawn", severity).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Process head position for distraction detection.
    /// </summary>
    private void ProcessHeadPosition(FaceDetectionResult face)
    {
        if (face.FaceAttributes?.HeadPose == null) 
            return;
        
        double yaw = face.FaceAttributes.HeadPose.Yaw;
        bool isHeadTurned = Math.Abs(yaw) > HEAD_TURN_THRESHOLD_DEGREES;
        
        if (isHeadTurned)
        {
            HandleHeadTurned(yaw);
        }
        else
        {
            if (_metrics.IsHeadTurned)
            {
                // Record response to head turn interventions as effective
                _interventionManager.RecordInterventionResponseAsync("HeadTurn", true).ConfigureAwait(false);
            }
            
            _metrics.IsHeadTurned = false;
            _metrics.HeadTurnStartTime = DateTime.MinValue;
            CurrentState.IsHeadTurned = false;
        }
    }

    /// <summary>
    /// Handles logic when head is detected as turned.
    /// </summary>
    private void HandleHeadTurned(double yawAngle)
    {
        if (!_metrics.IsHeadTurned)
        {
            _metrics.IsHeadTurned = true;
            _metrics.HeadTurnStartTime = DateTime.Now;
        }
        else
        {
            TimeSpan turnDuration = DateTime.Now - _metrics.HeadTurnStartTime;
            
            // Check if head has been turned too long
            if (turnDuration.TotalSeconds >= HEAD_TURN_DURATION_THRESHOLD_SECONDS)
            {
                DetectHeadTurnEvent();
                // Reset timer but keep tracking
                _metrics.HeadTurnStartTime = DateTime.Now;
            }
        }
        
        CurrentState.IsHeadTurned = true;
    }

    /// <summary>
    /// Records a head turn event.
    /// </summary>
    private void DetectHeadTurnEvent()
    {
        string eventTimestamp = DateTime.Now.ToString("HH:mm:ss");
        string headTurnMsg = $"Head turn detected for more than {HEAD_TURN_DURATION_THRESHOLD_SECONDS} seconds at {eventTimestamp}";
        _logger.LogWarning(headTurnMsg);
        
        // Log event
        _eventTracker.LogEvent($"HeadTurn at {eventTimestamp}");
        
        // Add to recent events if not duplicated
        AddEventIfNotRecent("HeadTurn", eventTimestamp);
        
        // Notify UI
        _hubContext.Clients.All.SendAsync("HeadTurnDetected").Wait();
        
        // Calculate severity based on duration
        TimeSpan turnDuration = DateTime.Now - _metrics.HeadTurnStartTime;
        double severity = Math.Min(turnDuration.TotalSeconds / HEAD_TURN_DURATION_THRESHOLD_SECONDS, 1.0);
        
        // Use intervention manager for adaptive intervention selection
        _interventionManager.HandleFatigueEventAsync("HeadTurn", severity).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles the case when no face is detected.
    /// </summary>
    private async Task HandleNoFaceDetectedAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("No faces detected");
        CurrentState.IsFaceVisible = false;
        
        if (_eventTracker.NoFaceStartTime == DateTime.MinValue)
            _eventTracker.NoFaceStartTime = DateTime.Now;
        
        TimeSpan noFaceDuration = DateTime.Now - _eventTracker.NoFaceStartTime;
        
        // Check if we've gone past the threshold for no face detected and it's time to alert
        if (noFaceDuration.TotalSeconds >= NO_FACE_THRESHOLD_SECONDS)
        {
            // Check if we're within the cooldown period after the last alert
            bool isWithinCooldown = (DateTime.Now - _lastNoFaceAlertTime).TotalSeconds < NO_FACE_ALERT_COOLDOWN_SECONDS;
            
            if (!isWithinCooldown)
            {
                string eventTimestamp = DateTime.Now.ToString("HH:mm:ss");
                string eventMsg = $"NoFaceDetected for {NO_FACE_THRESHOLD_SECONDS}s at {eventTimestamp}";
                
                _eventTracker.LogEvent(eventMsg);
                CurrentState.RecentEvents.Add(eventMsg);
                _logger.LogWarning(eventMsg);
                
                await _hubContext.Clients.All.SendAsync("NoFaceDetected", cancellationToken: cancellationToken);
                
                // Calculate severity based on duration without face
                double severity = Math.Min(noFaceDuration.TotalSeconds / (NO_FACE_THRESHOLD_SECONDS * 1.5), 1.0);
                
                // Use intervention manager for adaptive intervention selection
                await _interventionManager.HandleFatigueEventAsync("NoFaceDetected", severity);
                
                // Update the last alert time to enforce the cooldown period
                _lastNoFaceAlertTime = DateTime.Now;
                _eventTracker.IsNoFaceAlerted = true;
                
                _logger.LogInformation("No face alert triggered - next alert will be available after {time}", 
                    _lastNoFaceAlertTime.AddSeconds(NO_FACE_ALERT_COOLDOWN_SECONDS).ToString("HH:mm:ss"));
            }
            else
            {
                // We're within the cooldown period, log but don't alert
                _logger.LogDebug("No face still not detected but within cooldown period. Next alert at: {time}", 
                    _lastNoFaceAlertTime.AddSeconds(NO_FACE_ALERT_COOLDOWN_SECONDS).ToString("HH:mm:ss"));
            }
        }
    }

    /// <summary>
    /// Handles the case when a face is detected again after no-face state.
    /// </summary>
    private void HandleFaceDetected()
    {
        if (!CurrentState.IsFaceVisible && _eventTracker.IsNoFaceAlerted)
        {
            // Record response to no face interventions as effective
            _interventionManager.RecordInterventionResponseAsync("NoFaceDetected", true).ConfigureAwait(false);
        }
        
        CurrentState.IsFaceVisible = true;
        _eventTracker.LastFaceVisibleTime = DateTime.Now;
        _eventTracker.NoFaceStartTime = DateTime.MinValue;
        _eventTracker.IsNoFaceAlerted = false;
        
        // We don't reset _lastNoFaceAlertTime here to maintain the cooldown period
        // even if the face momentarily reappears and then disappears again
    }

    #endregion

    #region Private Methods - Utility

    /// <summary>
    /// Resets all state variables to default values.
    /// </summary>
    private void ResetState()
    {
        // Reset metrics tracking
        _metrics.Reset();
        _eventTracker.Reset();
        _lastNoFaceAlertTime = DateTime.MinValue; // Reset the alert cooldown timer
        
        // Reset UI state
        CurrentState.BlinkCount = 0;
        CurrentState.SleepyCount = 0;
        CurrentState.YawnCount = 0;
        CurrentState.IsCalibrated = false;
        CurrentState.IsFaceVisible = true;
        CurrentState.IsDriverSleepy = false;
        CurrentState.IsHeadTurned = false;
        CurrentState.RecentEvents.Clear();
    }

    /// <summary>
    /// Synchronizes internal state with CurrentState and notifies clients.
    /// </summary>
    private async Task SynchronizeStateAsync(CancellationToken cancellationToken)
    {
        // Update current state from metrics
        CurrentState.BlinkCount = _metrics.BlinkCount;
        CurrentState.SleepyCount = _metrics.SleepyCount;
        CurrentState.YawnCount = _metrics.YawnCount;
        CurrentState.IsCalibrated = _metrics.IsCalibrated;
        CurrentState.IsHeadTurned = _metrics.IsHeadTurned;
        
        // Calculate current sleepy state based on eye closure
        CurrentState.IsDriverSleepy = _metrics.AreEyesClosed && 
            (DateTime.Now - _metrics.EyeClosureStartTime).TotalSeconds >= DEFAULT_SLEEPY_THRESHOLD_SECONDS;
        
        try
        {
            // Send state update to all connected clients
            await _hubContext.Clients.All.SendAsync(
                "StateUpdated", 
                CurrentState, 
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending state update via SignalR");
            _logger.LogWarning("Failed to send state update - SleepyCount: {SleepyCount}", CurrentState.SleepyCount);
        }
    }

    /// <summary>
    /// Gets coaching advice from Azure OpenAI using the ChatCompletions API.
    /// </summary>
    private async Task<string> GetCoachingAdviceAsync(string summary, CancellationToken cancellationToken)
    {
        try
        {
            // Create chat completion options for the GPT-4.1 model
            var chatOptions = new ChatCompletionsOptions
            {
                DeploymentName = _deploymentName,
                MaxTokens = 100,
                Temperature = 0.7f,
            };

            // Add system message - defines the behavior of the assistant
            chatOptions.Messages.Add(new ChatMessage(ChatRole.System, @"You are a professional driver fatigue advisor.
Only speak if there are clear signs of driver fatigue (sleepiness, frequent yawning) 
or lack of concentration (driver not looking forward for 5+ seconds).
If both issues exist, combine into one calm advisory.
If repeated fatigue signals persist, escalate (suggest pulling over).
If no issues, respond with an empty string."));

            // Add user message with the driver status summary
            chatOptions.Messages.Add(new ChatMessage(ChatRole.User, $"Driver status: {summary}"));

            // Call the Chat Completions API
            var response = await _openAIClient.GetChatCompletionsAsync(
                chatOptions,
                cancellationToken);

            string coaching = response.Value.Choices[0].Message.Content.Trim();
            
            _logger.LogDebug("Received coaching response from GPT-4.1: {Response}", coaching);
            
            return coaching;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting coaching advice from Azure OpenAI GPT-4.1");
            
            // Log detailed error information
            if (ex is RequestFailedException rfe)
            {
                _logger.LogError("Azure OpenAI API error: Status {Status}, Message: {Message}", 
                    rfe.Status, rfe.Message);
            }
            
            return "I'm unable to provide coaching advice at the moment.";
        }
    }

    /// <summary>
    /// Adds an event to recent events list if a similar event isn't already there.
    /// </summary>
    private void AddEventIfNotRecent(string eventType, string timestamp)
    {
        if (!CurrentState.RecentEvents.Any(e => 
            e.Contains(eventType) && 
            (DateTime.Now - DateTime.Parse(e.Split(' ').Last())).TotalSeconds < EVENT_DISPLAY_COOLDOWN_SECONDS))
        {
            CurrentState.RecentEvents.Add($"{eventType} at {timestamp}");
        }
    }

    /// <summary>
    /// Properly disposes resources used by the service.
    /// </summary>
    private async Task DisposeResourcesAsync()
    {
        _capture?.Dispose();
        _frame?.Dispose();
        
        _capture = null;
        _frame = null;
    }

    /// <summary>
    /// Calculates Eye Aspect Ratio (EAR).
    /// </summary>
    private static double ComputeEAR(FaceLandmarks lm, bool isLeftEye)
    {
        var top = isLeftEye ? lm.EyeLeftTop : lm.EyeRightTop;
        var bottom = isLeftEye ? lm.EyeLeftBottom : lm.EyeRightBottom;
        var inner = isLeftEye ? lm.EyeLeftInner : lm.EyeRightInner;
        var outer = isLeftEye ? lm.EyeLeftOuter : lm.EyeRightOuter;
        
        if (top == null || bottom == null || inner == null || outer == null) 
            return 1;
        
        double vertical = Distance(top, bottom);
        double horizontal = Distance(inner, outer);
        return vertical / horizontal;
    }

    /// <summary>
    /// Draws boxes around eyes for visualization.
    /// </summary>
    private static void DrawEyeBoxes(FaceLandmarks lm, Mat frame)
    {
        int w = 60, h = 35;
        var leftRect = new Rect((int)lm.EyeLeftOuter.X - w / 2, (int)lm.EyeLeftOuter.Y - h / 2, w, h);
        var rightRect = new Rect((int)lm.EyeRightOuter.X - w / 2, (int)lm.EyeRightOuter.Y - h / 2, w, h);
        Cv2.Rectangle(frame, leftRect, new Scalar(0, 255, 0), 2);
        Cv2.Rectangle(frame, rightRect, new Scalar(0, 255, 0), 2);
    }

    /// <summary>
    /// Draws box around mouth for visualization.
    /// </summary>
    private static void DrawMouthBox(FaceLandmarks lm, Mat frame)
    {
        int w = 80, h = 50;
        int centerX = (int)((lm.UpperLipTop.X + lm.UnderLipBottom.X) / 2);
        int centerY = (int)((lm.UpperLipTop.Y + lm.UnderLipBottom.Y) / 2);
        var mouthRect = new Rect(centerX - w / 2, centerY - h / 2, w, h);
        Cv2.Rectangle(frame, mouthRect, new Scalar(255, 0, 0), 2);
    }

    /// <summary>
    /// Calculates the Euclidean distance between two points.
    /// </summary>
    private static double Distance(dynamic p1, dynamic p2)
    {
        double dx = p1.X - p2.X, dy = p1.Y - p2.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// Disposes resources used by this service.
    /// </summary>
    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _capture?.Dispose();
        _frame?.Dispose();
        _speechSynthesizer.Dispose();
        
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Nested Helper Classes

    /// <summary>
    /// Encapsulates driver metrics and state tracking.
    /// </summary>
    private class DriverMetrics
    {
        // Eye metrics
        public double BaselineEAR { get; set; }
        public bool IsCalibrated { get; set; }
        public ConcurrentQueue<double> EarBuffer { get; } = new();
        public bool AreEyesClosed { get; set; }
        public DateTime EyeClosureStartTime { get; set; }
        public DateTime LastSleepyDetectionTime { get; set; }
        
        // Mouth metrics
        public bool IsMouthHeldOpen { get; set; }
        public DateTime MouthOpenStartTime { get; set; }
        public bool IsYawnInProgress { get; set; }
        
        // Head position
        public bool IsHeadTurned { get; set; }
        public DateTime HeadTurnStartTime { get; set; }
        
        // Counters
        public int BlinkCount { get; set; }
        public int SleepyCount { get; set; }
        public int YawnCount { get; set; }

        /// <summary>
        /// Resets all metrics to default values.
        /// </summary>
        public void Reset()
        {
            BaselineEAR = 0.0;
            IsCalibrated = false;
            EarBuffer.Clear();
            AreEyesClosed = false;
            EyeClosureStartTime = DateTime.MinValue;
            LastSleepyDetectionTime = DateTime.MinValue;
            
            IsMouthHeldOpen = false;
            MouthOpenStartTime = DateTime.MinValue;
            IsYawnInProgress = false;
            
            IsHeadTurned = false;
            HeadTurnStartTime = DateTime.MinValue;
            
            BlinkCount = 0;
            SleepyCount = 0;
            YawnCount = 0;
        }
    }

    /// <summary>
    /// Encapsulates event tracking and summary generation.
    /// </summary>
    private class EventTracker
    {
        private readonly ConcurrentQueue<string> _events = new();
        
        public DateTime LastFaceVisibleTime { get; set; } = DateTime.MinValue;
        public DateTime NoFaceStartTime { get; set; } = DateTime.MinValue;
        public bool IsNoFaceAlerted { get; set; }

        /// <summary>
        /// Logs an event with timestamp.
        /// </summary>
        public void LogEvent(string eventText)
        {
            _events.Enqueue(eventText);
        }

        /// <summary>
        /// Generates a summary of recent events (last minute).
        /// </summary>
        public string GenerateSummary()
        {
            var now = DateTime.Now;
            
            // Remove events older than 1 minute
            while (_events.TryPeek(out var e) &&
                   (now - DateTime.Parse(e.Split(' ').Last())).TotalMinutes > 1)
            {
                _events.TryDequeue(out _);
            }
            
            // Return empty summary if no events
            if (_events.Count == 0) return string.Empty;
            
            // Generate summary of events by type
            return $"In the last minute: " +
                   $"{_events.Count(e => e.Contains("Yawn"))} yawns, " +
                   $"{_events.Count(e => e.Contains("HeadTurn"))} times driver wasn't looking ahead 5+ seconds, " +
                   $"Found sleepy {_events.Count(e => e.Contains("Sleepy"))} times, " +
                   $"{_events.Count(e => e.Contains("NoFaceDetected"))} times no face detected for 15s.";
        }

        /// <summary>
        /// Resets the event tracker.
        /// </summary>
        public void Reset()
        {
            _events.Clear();
            LastFaceVisibleTime = DateTime.MinValue;
            NoFaceStartTime = DateTime.MinValue;
            IsNoFaceAlerted = false;
        }
    }

    #endregion
}