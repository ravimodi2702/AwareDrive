# AwareDrive: Agentic AI Driver Fatigue Monitoring System

A cutting-edge, real-time driver fatigue monitoring system powered by agentic AI that detects and proactively responds to signs of driver fatigue, including eye closure patterns, yawning frequency, head position tracking, and face presence monitoring.

![AwareDrive Dashboard](./wwwroot/img/dashboard-preview.png)

## 🚀 Key Features

- **Multi-Modal Fatigue Detection**:
  - Eye closure monitoring with dynamic calibration
  - Yawn detection with pattern recognition
  - Head position tracking for distraction detection
  - Face presence verification

- **Agentic AI System**:
  - Autonomous decision-making for intervention selection
  - Adaptive learning from driver responses
  - Personalized intervention strategies
  - Context-aware fatigue assessment

- **Adaptive Intervention Framework**:
  - Multi-level audio alerts (mild, moderate, urgent)
  - Visual dashboard warnings
  - AI-generated coaching advice
  - Effectiveness tracking and optimization

- **Real-time Monitoring Dashboard**:
  - Live video feed with feature visualization
  - Comprehensive metrics and statistics
  - Chronological event tracking
  - Dynamic status indicators

## 🛠️ Technology Stack

- **Backend**: .NET 8, C# 12.0, ASP.NET Core
- **Computer Vision**: Azure Face API, OpenCvSharp
- **AI and ML**: Azure OpenAI (GPT-4.1)
- **Real-time Communication**: SignalR
- **User Interface**: HTML5, CSS3, JavaScript, Bootstrap 5
- **Speech Synthesis**: System.Speech

## 📋 Requirements

- .NET 8.0 SDK or later
- Webcam
- Azure Face API subscription
- Azure OpenAI API subscription
- Modern web browser (Chrome, Firefox, Edge)

## 🔧 Installation

1. Clone the repository:git clone https://github.com/ravimodi2702/AwareDrive.git
cd AwareDrive
2. Update the `appsettings.json` file with your Azure API keys:{
  "Azure": {
    "FaceApi": {
      "Endpoint": "YOUR_FACE_API_ENDPOINT",
      "ApiKey": "YOUR_FACE_API_KEY"
    },
    "OpenAI": {
      "Endpoint": "YOUR_OPENAI_ENDPOINT",
      "ApiKey": "YOUR_OPENAI_API_KEY",
      "DeploymentName": "YOUR_DEPLOYMENT_NAME"
    }
  }
   }
3. Build and run the project:dotnet build
   dotnet run
4. Open your browser and navigate to `http://localhost:63606`

## 🖥️ Usage

1. Click "Start Monitoring" to begin driver fatigue detection
2. Position yourself in front of the camera
3. The system will automatically calibrate to your baseline eye metrics
4. Monitor the dashboard for fatigue indicators in real-time
5. Receive AI coaching advice when signs of fatigue are detected
6. View recent events and interventions in the events panel

## 🧠 How It Works

1. **Face Detection**: Azure Face API detects facial landmarks and head pose
2. **Eye Analysis**: Calculates Eye Aspect Ratio (EAR) to detect blinks and prolonged closure
3. **Mouth Analysis**: Measures mouth openness ratios to identify yawning
4. **Head Position**: Tracks head rotation to detect distraction
5. **Intervention Selection**: Agentic AI selects appropriate interventions based on:
   - Event type and severity
   - Driver's historical response patterns
   - Intervention effectiveness history
6. **Adaptive Learning**: System continuously updates effectiveness scores to improve future interventions

## 🌐 API Endpoints

- `POST /api/DriverMonitoring/start`: Start the monitoring process
- `POST /api/DriverMonitoring/stop`: Stop the monitoring process
- `GET /api/DriverMonitoring/state`: Get the current monitoring state
- `GET /api/DriverMonitoring/frame`: Get the latest camera frame
- `POST /api/DriverMonitoring/reset-interventions`: Reset intervention effectiveness scores

## 📊 Architecture

The system follows a modular architecture with clear separation of concerns:

- **Core Services**:
  - `DriverMonitoringService`: Main monitoring and detection logic
  - `InterventionManager`: Manages selection and delivery of interventions
  - `DriverProfileStorageService`: Handles persistence of driver profiles

- **Real-time Communication**:
  - `MonitoringHub`: SignalR hub for real-time updates
  - `DriverMonitoringController`: REST API endpoints

## 🔍 Future Enhancements

- Integration with vehicle systems for automatic speed reduction
- Mobile application support
- Machine learning to improve detection accuracy over time
- Fleet management dashboard for transportation companies
- Integration with wearable devices for additional biometric monitoring

## 📝 License

MIT License

## 🏆 Project Tags

#AwareDrive #AgenticAI #DriverSafety #AzureFaceAPI #AccidentPrevention #ComputerVision #DotNet8
