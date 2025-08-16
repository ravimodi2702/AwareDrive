using FacePOC.Hubs;
using FacePOC.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR
builder.Services.AddSignalR();

// Register driver profile storage and intervention manager as singletons
builder.Services.AddSingleton<DriverProfileStorageService>();
builder.Services.AddSingleton<InterventionManager>();

// Register driver monitoring service as singleton
builder.Services.AddSingleton<DriverMonitoringService>();

// Add CORS policy
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Reset driver profile on startup for testing
using (var scope = app.Services.CreateScope())
{
    var profileStorage = scope.ServiceProvider.GetRequiredService<DriverProfileStorageService>();
    profileStorage.DeleteProfile("default");
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();

// Map controllers and SignalR hub
app.MapControllers();
app.MapHub<MonitoringHub>("/monitoringHub");

// Serve static frontend files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();