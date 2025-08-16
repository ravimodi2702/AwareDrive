# Driver Fatigue Monitoring System

A real-time driver fatigue monitoring system that uses computer vision and AI to detect signs of driver fatigue, such as eye blink patterns, yawning, and head position.

## Features

- Face detection and tracking using Azure Face API
- Eye blink detection and analysis
- Yawn detection
- Head position tracking
- Real-time monitoring with visual alerts
- AI-powered coaching advice using Azure OpenAI
- Web-based monitoring dashboard

## Requirements

- .NET 8.0 or later
- Webcam
- Azure Face API subscription
- Azure OpenAI API subscription
- Modern web browser (Chrome, Firefox, Edge)

## Installation

1. Clone the repository
2. Update the `appsettings.json` file with your Azure API keys
3. Build the project: `dotnet build`
4. Run the application: `dotnet run`
5. Open your browser and navigate to `https://localhost:5001`

## Usage

1. Click "Start Monitoring" to begin the driver fatigue monitoring
2. Position yourself in front of the camera
3. The system will calibrate automatically
4. Monitor the dashboard for fatigue indicators
5. AI coaching advice will appear when signs of fatigue are detected
6. Click "Stop Monitoring" to end the session

## API Endpoints

- `POST /api/DriverMonitoring/start`: Start the monitoring process
- `POST /api/DriverMonitoring/stop`: Stop the monitoring process
- `GET /api/DriverMonitoring/state`: Get the current monitoring state
- `GET /api/DriverMonitoring/frame`: Get the latest camera frame

## Technology Stack

- ASP.NET Core 8.0
- SignalR for real-time communication
- OpenCvSharp for image processing
- Azure Face API for face detection and analysis
- Azure OpenAI for intelligent coaching
- HTML/CSS/JavaScript for the frontend

## License

MIT License"# AwareDrive" 

## Configuration

 "Azure": {
    "FaceApi": {
      "Endpoint": "**********",
      "ApiKey": "************"
    },
    "OpenAI": {
      "Endpoint": "**********",
      "ApiKey": "**********",
      "DeploymentName": "********"
    }
  }