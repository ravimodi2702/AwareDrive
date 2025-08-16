// DOM elements
const btnStart = document.getElementById('btnStart');
const btnStop = document.getElementById('btnStop');
const cameraFeed = document.getElementById('cameraFeed');
const noFaceOverlay = document.getElementById('noFaceOverlay');
const sleepyOverlay = document.getElementById('sleepyOverlay');
const sleepyCount = document.getElementById('sleepyCount');
const yawnCount = document.getElementById('yawnCount');
const calibrationStatus = document.getElementById('calibrationStatus');
const faceStatus = document.getElementById('faceStatus');
const headStatus = document.getElementById('headStatus');
const coachingAdvice = document.getElementById('coachingAdvice');
const eventsList = document.getElementById('eventsList');
const metricsContainer = document.getElementById('metricsContainer');
const initialMessage = document.getElementById('initialMessage');

// Create placeholder image for camera feed
function createPlaceholderImage() {
    const canvas = document.createElement('canvas');
    canvas.width = 640;
    canvas.height = 480;
    const ctx = canvas.getContext('2d');
    
    // Fill background
    ctx.fillStyle = '#f0f0f0';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    
    // Add text
    ctx.fillStyle = '#888888';
    ctx.font = '24px Arial';
    ctx.textAlign = 'center';
    ctx.fillText('Camera feed is not active', canvas.width / 2, canvas.height / 2 - 20);
    ctx.fillText('Click "Start Monitoring" to begin', canvas.width / 2, canvas.height / 2 + 20);
    
    // Convert to data URL
    return canvas.toDataURL('image/png');
}

// Set initial placeholder image
cameraFeed.src = createPlaceholderImage();

// API endpoints
const apiBaseUrl = '';  // Use relative URL for same-origin
const startEndpoint = `${apiBaseUrl}/api/DriverMonitoring/start`;
const stopEndpoint = `${apiBaseUrl}/api/DriverMonitoring/stop`;
const stateEndpoint = `${apiBaseUrl}/api/DriverMonitoring/state`;
const frameEndpoint = `${apiBaseUrl}/api/DriverMonitoring/frame`;

// SignalR connection
let connection = null;
let isMonitoring = false;
let frameUpdateInterval = null;

// Debug flag
const DEBUG = true;
function debugLog(...args) {
    if (DEBUG) {
        console.log('[DEBUG]', ...args);
    }
}

// Initialize the application
document.addEventListener('DOMContentLoaded', () => {
    debugLog('DOM loaded, initializing application');
    initializeSignalR();
    setupEventListeners();
});

// Initialize SignalR connection
function initializeSignalR() {
    debugLog('Initializing SignalR connection');
    
    if (typeof signalR === 'undefined') {
        console.error('SignalR library not loaded!');
        setTimeout(initializeSignalR, 2000); // Try again in 2 seconds
        return;
    }

    connection = new signalR.HubConnectionBuilder()
        .withUrl(`${apiBaseUrl}/monitoringHub`)
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // SignalR event handlers
    connection.on('MonitoringStarted', () => {
        debugLog('SignalR: Monitoring started received');
        updateUIState(true);
    });

    connection.on('MonitoringStopped', () => {
        debugLog('SignalR: Monitoring stopped received');
        updateUIState(false);
    });

    connection.on('StateUpdated', (state) => {
        debugLog('SignalR: State updated received', state);
        updateUIWithState(state);
    });

    connection.on('NoFaceDetected', () => {
        debugLog('SignalR: No face detected received');
        showNoFaceAlert();
    });

    connection.on('DriverSleepy', () => {
        debugLog('SignalR: Driver sleepy received');
        showSleepyAlert();
    });

    connection.on('YawnDetected', () => {
        debugLog('SignalR: Yawn detected received');
        addEvent('Yawn detected');
    });

    connection.on('HeadTurnDetected', () => {
        debugLog('SignalR: Head turn detected received');
        addEvent('Head turned away for too long');
    });

    connection.on('CoachingReceived', (advice) => {
        debugLog('SignalR: Coaching received', advice);
        updateCoachingAdvice(advice);
    });

    // Add connection state change handlers
    connection.onreconnecting(error => {
        console.warn('SignalR connection lost. Reconnecting...', error);
    });
    
    connection.onreconnected(connectionId => {
        console.log('SignalR reconnected with ID:', connectionId);
        // Refresh state after reconnection
        fetchCurrentState();
    });
    
    connection.onclose(error => {
        console.error('SignalR connection closed', error);
        setTimeout(initializeSignalR, 5000); // Try to reconnect after 5 seconds
    });

    // Start the connection
    connection.start()
        .then(() => {
            console.log('SignalR Connected successfully with ID:', connection.connectionId);
            // Check if monitoring is active when page loads
            fetchCurrentState();
        })
        .catch(err => {
            console.error('SignalR Connection Error: ', err);
            setTimeout(initializeSignalR, 5000); // Retry after 5 seconds
        });
}

// Request current state from the SignalR hub
function requestStateFromHub() {
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        debugLog('Manually requesting current state from hub');
        connection.invoke('RequestCurrentState')
            .then(() => {
                debugLog('State request sent successfully');
            })
            .catch(err => {
                console.error('Error requesting state:', err);
            });
    } else {
        debugLog('Cannot request state: SignalR not connected');
    }
}

// Set up event listeners
function setupEventListeners() {
    debugLog('Setting up event listeners');
    btnStart.addEventListener('click', startMonitoring);
    btnStop.addEventListener('click', stopMonitoring);
    
    // Add a keyboard shortcut (Ctrl+R) to manually request state update
    document.addEventListener('keydown', function(e) {
        if (e.ctrlKey && e.key === 'r') {
            e.preventDefault(); // Prevent browser refresh
            requestStateFromHub();
            debugLog('Manual state refresh requested (Ctrl+R)');
        }
    });
}

// Start monitoring
async function startMonitoring() {
    debugLog('Starting monitoring');
    try {
        const response = await fetch(startEndpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });

        if (response.ok) {
            const result = await response.json();
            debugLog('Monitoring started successfully', result);
            startFrameUpdates();
            
            // Show metrics container and hide initial message
            showMetricsPanel();
            
            // Give the server a moment to initialize everything
            setTimeout(() => {
                requestStateFromHub();
            }, 1000);
        } else {
            const error = await response.json();
            console.error('Error starting monitoring:', error);
            alert('Failed to start monitoring. Please check the console for details.');
        }
    } catch (error) {
        console.error('Error starting monitoring:', error);
        alert('Failed to start monitoring. Please check the console for details.');
    }
}

// Stop monitoring
async function stopMonitoring() {
    debugLog('Stopping monitoring');
    try {
        const response = await fetch(stopEndpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });

        if (response.ok) {
            const result = await response.json();
            debugLog('Monitoring stopped successfully', result);
            stopFrameUpdates();
            
            // Hide metrics container and show initial message when monitoring is stopped
            hideMetricsPanel();
        } else {
            const error = await response.json();
            console.error('Error stopping monitoring:', error);
            alert('Failed to stop monitoring. Please check the console for details.');
        }
    } catch (error) {
        console.error('Error stopping monitoring:', error);
        alert('Failed to stop monitoring. Please check the console for details.');
    }
}

// Show metrics panel and hide initial message
function showMetricsPanel() {
    metricsContainer.style.display = 'block';
    initialMessage.style.display = 'none';
}

// Hide metrics panel and show initial message
function hideMetricsPanel() {
    metricsContainer.style.display = 'none';
    initialMessage.style.display = 'block';
}

// Fetch current state from API
async function fetchCurrentState() {
    debugLog('Fetching current state');
    try {
        const response = await fetch(stateEndpoint);
        
        if (response.ok) {
            const state = await response.json();
            debugLog('Current state received:', state);
            updateUIWithState(state);
            
            if (state.hasFrame) {
                updateUIState(true);
                startFrameUpdates();
                showMetricsPanel(); // Show metrics if monitoring is active
            } else {
                updateUIState(false);
                hideMetricsPanel(); // Hide metrics if monitoring is not active
            }
        } else {
            console.error('Error fetching state:', await response.text());
        }
    } catch (error) {
        console.error('Error fetching state:', error);
    }
}

// Update UI with the current state
function updateUIWithState(state) {
    debugLog('Updating UI with state:', state);
    
    // First log specifically what we're trying to update
    debugLog('sleepyCount value to be set:', state.sleepyCount);
    debugLog('yawnCount value to be set:', state.yawnCount);
    
    // Update counters
    sleepyCount.textContent = state.sleepyCount || 0;
    yawnCount.textContent = state.yawnCount || 0;
    
    // Add danger class if sleepiness is detected
    if (state.sleepyCount > 0) {
        sleepyCount.classList.add('danger-text');
    } else {
        sleepyCount.classList.remove('danger-text');
    }
    
    if (state.isCalibrated) {
        calibrationStatus.textContent = 'Calibrated';
        calibrationStatus.classList.add('success-text');
    } else {
        calibrationStatus.textContent = 'Not Calibrated';
        calibrationStatus.classList.remove('success-text');
    }
    
    faceStatus.textContent = state.isFaceVisible ? 'Yes' : 'No';
    faceStatus.className = state.isFaceVisible ? 'metric-value success-text' : 'metric-value danger-text';
    
    headStatus.textContent = state.isHeadTurned ? 'Turned Away' : 'Centered';
    headStatus.className = state.isHeadTurned ? 'metric-value warning-text' : 'metric-value success-text';
    
    if (state.lastCoachingAdvice) {
        updateCoachingAdvice(state.lastCoachingAdvice);
    }
    
    // Update alerts
    noFaceOverlay.classList.toggle('d-none', state.isFaceVisible);
    sleepyOverlay.classList.toggle('d-none', !state.isDriverSleepy);
    
    // Update recent events
    if (state.recentEvents && state.recentEvents.length > 0) {
        eventsList.innerHTML = '';
        state.recentEvents.forEach(event => {
            addEvent(event, false); // Don't add timestamp as events already have them
        });
    }
    
    // Log what we ended up setting in the DOM
    debugLog('DOM updated. Current sleepyCount text:', sleepyCount.textContent);
}

// Start frame updates
function startFrameUpdates() {
    debugLog('Starting frame updates');
    isMonitoring = true;
    updateUIState(true);
    
    if (frameUpdateInterval) {
        clearInterval(frameUpdateInterval);
    }
    
    // Update camera feed every 200ms (5fps)
    frameUpdateInterval = setInterval(() => {
        updateCameraFeed();
    }, 200);
}

// Stop frame updates
function stopFrameUpdates() {
    debugLog('Stopping frame updates');
    isMonitoring = false;
    updateUIState(false);
    
    if (frameUpdateInterval) {
        clearInterval(frameUpdateInterval);
        frameUpdateInterval = null;
    }
    
    // Reset camera feed
    cameraFeed.src = createPlaceholderImage();
}

// Update camera feed with the latest frame
function updateCameraFeed() {
    if (isMonitoring) {
        const timestamp = new Date().getTime();
        cameraFeed.src = `${frameEndpoint}?t=${timestamp}`;
    }
}

// Update UI state based on monitoring status
function updateUIState(monitoring) {
    debugLog('Updating UI state, monitoring:', monitoring);
    isMonitoring = monitoring;
    btnStart.disabled = monitoring;
    btnStop.disabled = !monitoring;
    
    // Show/hide metrics based on monitoring status
    if (monitoring) {
        showMetricsPanel();
    } else {
        hideMetricsPanel();
        noFaceOverlay.classList.add('d-none');
        sleepyOverlay.classList.add('d-none');
    }
}

// Update coaching advice
function updateCoachingAdvice(advice) {
    debugLog('Updating coaching advice:', advice);
    coachingAdvice.textContent = advice || 'No advice at the moment...';
    
    // Add pulse effect if there is advice
    if (advice) {
        coachingAdvice.classList.add('pulse');
        setTimeout(() => {
            coachingAdvice.classList.remove('pulse');
        }, 5000);
    }
}

// Add event to the events list
function addEvent(eventText, addTimestamp = true) {
    const li = document.createElement('li');
    li.className = 'list-group-item';
    
    // Highlight sleepy events in red
    if (eventText.includes('Sleepy')) {
        li.classList.add('list-group-item-danger');
    } else if (eventText.includes('Yawn')) {
        li.classList.add('list-group-item-warning');
    } else if (eventText.includes('HeadTurn')) {
        li.classList.add('list-group-item-info');
    }
    
    if (addTimestamp) {
        const timestamp = new Date().toLocaleTimeString();
        li.textContent = `[${timestamp}] ${eventText}`;
    } else {
        li.textContent = eventText;
    }
    
    // Add to top of list
    if (eventsList.firstChild) {
        eventsList.insertBefore(li, eventsList.firstChild);
    } else {
        eventsList.appendChild(li);
    }
    
    // Limit to 10 events
    if (eventsList.children.length > 10) {
        eventsList.removeChild(eventsList.lastChild);
    }
}

// Show no face alert
function showNoFaceAlert() {
    debugLog('Showing no face alert');
    noFaceOverlay.classList.remove('d-none');
    setTimeout(() => {
        if (isMonitoring) {
            noFaceOverlay.classList.add('d-none');
        }
    }, 3000);
}

// Show sleepy alert
function showSleepyAlert() {
    debugLog('Showing sleepy alert');
    sleepyOverlay.classList.remove('d-none');
    setTimeout(() => {
        if (isMonitoring) {
            sleepyOverlay.classList.add('d-none');
        }
    }, 3000);
}

// Add a manual check for state updates every 5 seconds as a fallback
setInterval(() => {
    if (isMonitoring) {
        debugLog('Performing periodic state check');
        fetchCurrentState();
    }
}, 5000);