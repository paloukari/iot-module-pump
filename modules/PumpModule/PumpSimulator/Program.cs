using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Edge.ModuleUtil;
using Microsoft.Azure.Devices.Edge.Util;
using Microsoft.Azure.Devices.Edge.Util.Concurrency;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PumpSimulator
{
    class Program
    {
        const string MessageCountConfigKey = "MessageCount";
        const string SendDataConfigKey = "SendData";
        const string SendIntervalConfigKey = "SendInterval";
        const string EventCountConfigKey = "EventCount";

        static readonly Guid BatchId = Guid.NewGuid();
        static readonly AtomicBoolean Reset = new AtomicBoolean(false);
        static readonly Random Rnd = new Random();

        static bool sendData = true;
        static bool insights = false;
        static bool debug = false;
        static TimeSpan messageDelay;
        static int eventCount = 1;
        static int messageCount;

        static TelemetryClient telemetryClient;
        static ModuleClient moduleClient;
        static TwinCollection reportedProperties;
        static TransportType protocol = TransportType.Mqtt_Tcp_Only;

        public static int Main() => MainAsync().Result;

        static async Task<int> MainAsync()
        {

            Console.WriteLine($"{DateTime.Now.ToLocalTime()}>\t The PumpSimulator Main() has started.");
            SetupTelemetry();

            try
            {
                RetrieveSettingsFromConfig();

                var simulatorParameters = SimulatorParameters.Create();
                (CancellationTokenSource cts, ManualResetEventSlim completed, Option<object> handler) = ShutdownHandler.Init(TimeSpan.FromSeconds(5), null);

                Console.WriteLine(
                    $"Initializing simulated sensor to send {(SendUnlimitedMessages(messageCount) ? "unlimited" : messageCount.ToString())} "
                    + $"messages, at an interval of {messageDelay.TotalSeconds} seconds.\n"
                    + $"To change this, set the environment variable {MessageCountConfigKey} to the number of messages that should be sent (set it to -1 to send unlimited messages).");

                moduleClient = await ModuleUtil.CreateModuleClientAsync(
                        protocol,
                        ModuleUtil.DefaultTimeoutErrorDetectionStrategy,
                        ModuleUtil.DefaultTransientRetryStrategy);
                ModuleClient userContext = moduleClient;
                reportedProperties = new TwinCollection();

                await moduleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdated, userContext);
                await moduleClient.SetMethodHandlerAsync("reset", ResetMethod, null);
                await moduleClient.SetMethodHandlerAsync("ping", PingMethod, null);
                await moduleClient.SetMethodHandlerAsync("ping2", Ping2Method, null);
                await moduleClient.SetInputMessageHandlerAsync("control", ControlMessageHandle, userContext);

                await RetrieveSettingsFromTwin(moduleClient);
                await SendEvents(moduleClient, messageCount, simulatorParameters, cts);
                await cts.Token.WhenCanceled();

                completed.Set();
                handler.ForEach(h => GC.KeepAlive(h));
                Console.WriteLine("SimulatedTemperatureSensor Main() finished.");
                return 0;
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"{DateTime.Now.ToLocalTime()}>\t PumpSimulator Main() error.");
                Console.WriteLine(ex.Message);
                var telemetry = new ExceptionTelemetry(ex);
                telemetryClient.TrackException(telemetry);
                return -1;
            }
        }

        private static void SetupTelemetry()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")))
            {
                insights = true;
                telemetryClient = new TelemetryClient();
                telemetryClient.Context.Device.Id = Environment.MachineName;
                telemetryClient.TrackEvent("Simulator started");
                telemetryClient.GetMetric("PumpCount").TrackValue(1);
                Console.WriteLine("Application Insights Enabled.");
            }
        }

        private static void RetrieveSettingsFromConfig()
        {
            var appSettings = ConfigurationManager.AppSettings;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOG_LEVEL")) && Environment.GetEnvironmentVariable("LOG_LEVEL") == "debug")
            {
                debug = true;
            }

            if (!TimeSpan.TryParse(appSettings["MessageDelay"], out messageDelay))
            {
                messageDelay = TimeSpan.FromSeconds(1000);
            }

            if (!TransportType.TryParse(appSettings["Protocol"], out protocol))
            {
                protocol = TransportType.Mqtt_Tcp_Only;
            }

            if (!int.TryParse(Environment.GetEnvironmentVariable(MessageCountConfigKey), out messageCount) && !int.TryParse(appSettings[MessageCountConfigKey], out messageCount))
            {
                messageCount = 500;
            }
        }

        private static async Task RetrieveSettingsFromTwin(ModuleClient moduleClient)
        {
            Twin currentTwinProperties = await moduleClient.GetTwinAsync();
            Console.WriteLine("Initialized Twin State Received");

            if (currentTwinProperties.Properties.Desired.Contains(SendIntervalConfigKey))
            {
                Console.WriteLine("SendInterval: " + currentTwinProperties.Properties.Desired[SendIntervalConfigKey]);
                var desiredInterval = (int)currentTwinProperties.Properties.Desired[SendIntervalConfigKey];
                messageDelay = TimeSpan.FromMilliseconds(desiredInterval);
            }

            if (currentTwinProperties.Properties.Desired.Contains(EventCountConfigKey))
            {
                Console.WriteLine("EventCount: " + currentTwinProperties.Properties.Desired[EventCountConfigKey]);
                var desiredCount = (int)currentTwinProperties.Properties.Desired[EventCountConfigKey];
                eventCount = desiredCount;
            }

            if (currentTwinProperties.Properties.Desired.Contains(SendDataConfigKey))
            {
                Console.WriteLine("SendData: " + currentTwinProperties.Properties.Desired[SendDataConfigKey]);
                sendData = (bool)currentTwinProperties.Properties.Desired[SendDataConfigKey];
                if (!sendData)
                {
                    Console.WriteLine("Sending data disabled. Change twin configuration to start sending again.");
                }
            }
        }

        /// <summary>
        /// Module behavior:
        ///        Sends data periodically (with default frequency of 5 seconds).
        ///        Data trend:
        ///         - Temperature regularly rises from 21C to 100C in regularly with jitter
        ///         - Pressure correlates with Temperature 1 to 10psi
        ///         - Suction Pressure stable around 5psi
        ///         - Discharge Pressure stable around 5psi
        ///         - Ambient temperature stable around 21C
        ///         - Flow is stable with tiny jitter around 25%
        ///                Method for resetting the data stream
        /// </summary>
        static async Task SendEvents(
            ModuleClient moduleClient,
            int messageCount,
            SimulatorParameters sim,
            CancellationTokenSource cts)
        {
            int count = 1;
            double currentTemp = sim.TempMin;
            double normal = (sim.PressureMax - sim.PressureMin) / (sim.TempMax - sim.TempMin);

            while (!cts.Token.IsCancellationRequested && (SendUnlimitedMessages(messageCount) || messageCount >= count))
            {
                if (Reset)
                {
                    currentTemp = sim.TempMin;
                    Reset.Set(false);
                }

                if (currentTemp > sim.TempMax)
                {
                    currentTemp += Rnd.NextDouble() - 0.5; // add value between [-0.5..0.5]
                }
                else
                {
                    currentTemp += -0.25 + (Rnd.NextDouble() * 1.5); // add value between [-0.25..1.25] - average +0.5
                }

                if (sendData)
                {
                    var events = new List<MessageEvent>();
                    var deviceId = Environment.MachineName + "-" + Environment.GetEnvironmentVariable("DEVICE");

                    // Add Desired Number of Events into the Message
                    for (int i = 0; i < eventCount; i++)
                    {
                        events.Add(new MessageEvent
                        {
                            DeviceId = deviceId + "-" + i,
                            TimeStamp = DateTime.UtcNow,
                            Temperature = new SensorReading
                            {
                                Value = currentTemp,
                                Units = "degC",
                                Status = 200
                            },
                            Pressure = new SensorReading
                            {
                                Value = sim.PressureMin + ((currentTemp - sim.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            SuctionPressure = new SensorReading
                            {
                                Value = sim.PressureMin + 4 + ((currentTemp - sim.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            DischargePressure = new SensorReading
                            {
                                Value = sim.PressureMin + 1 + ((currentTemp - sim.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            Flow = new SensorReading
                            {
                                Value = Rnd.Next(78, 82),
                                Units = "perc",
                                Status = 200
                            }
                        });
                        currentTemp += -0.25 + (Rnd.NextDouble() * 1.5);
                    }
                   
                    var msgBody = new MessageBody
                    {
                        Asset = Environment.GetEnvironmentVariable("ASSET") ?? "whidbey",
                        Source = Environment.MachineName,
                        Events = events
                    };

                    string dataBuffer = JsonConvert.SerializeObject(msgBody);
                    var eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));
                    eventMessage.Properties.Add("sequenceNumber", count.ToString());
                    eventMessage.Properties.Add("batchId", BatchId.ToString());
                    eventMessage.Properties.Add("asset", msgBody.Asset);
                    var size = eventMessage.GetBytes().Length;

                    if(debug) Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Size: {size}, Body: [{dataBuffer}]");
                    else Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Size: {size}");

                    try
                    {
                        if (insights)
                        {
                            telemetryClient.GetMetric("SendMessage").TrackValue(1);
                            telemetryClient.Context.Operation.Name = "Special Operation";
                            Metric sizeStats = telemetryClient.GetMetric("Special Operation Message Size");
                            sizeStats.TrackValue(size);
                        }
                        await moduleClient.SendEventAsync("temperatureOutput", eventMessage);  
                    }
                    catch (Microsoft.Azure.Devices.Client.Exceptions.MessageTooLargeException exception)
                    {
                        Console.WriteLine(exception.Message);
                        if (insights)
                        {
                            telemetryClient.GetMetric("MessageSizeExceeded").TrackValue(1);
                        }
                    }                    
                    count++;
                }

                await Task.Delay(messageDelay, cts.Token);
            }

            if (messageCount < count)
            {
                Console.WriteLine($"Done sending {messageCount} messages");
            }
        }

        static bool SendUnlimitedMessages(int maximumNumberOfMessages) => maximumNumberOfMessages < 0;

        
        // Control Message expected to be:
        // {
        //     "command" : "reset"
        // }
        static Task<MessageResponse> ControlMessageHandle(Message message, object userContext)
        {
            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);

            Console.WriteLine($"Received message Body: [{messageString}]");

            try
            {
                var messages = JsonConvert.DeserializeObject<ControlCommand[]>(messageString);

                foreach (ControlCommand messageBody in messages)
                {
                    if (messageBody.Command == ControlCommandEnum.Reset)
                    {
                        Console.WriteLine("Resetting temperature sensor..");
                        Reset.Set(true);
                    }
                }
            }
            catch (JsonSerializationException)
            {
                var messageBody = JsonConvert.DeserializeObject<ControlCommand>(messageString);

                if (messageBody.Command == ControlCommandEnum.Reset)
                {
                    Console.WriteLine("Resetting temperature sensor..");
                    Reset.Set(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to deserialize control command with exception: [{ex}]");
            }

            return Task.FromResult(MessageResponse.Completed);
        }

        static Task<MethodResponse> ResetMethod(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Received direct method call to reset temperature sensor...");
            Reset.Set(true);
            
            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            return Task.FromResult(response);
        }

        static Task<MethodResponse> Ping2Method(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Received direct method call to ping2 method...");


            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            return Task.FromResult(response);
        }

        static Task<MethodResponse> PingMethod(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine("Received Ping direct method call...");
            try
            {
                var directMethod = new TwinCollection();
                directMethod["PingTime"] = DateTime.Now;
                reportedProperties["DirectMethod"] = directMethod;
                Console.WriteLine("Reporting Module Twin Properties...");
                moduleClient.UpdateReportedPropertiesAsync(reportedProperties).Wait();
                Console.WriteLine("Updated Module Twin Properties...");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            return Task.FromResult(response);

            //return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes("Pong"), 200));
        }


        static async Task OnDesiredPropertiesUpdated(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine("Device Twin Update Received");

            // At this point just update the configure configuration.
            if (desiredProperties.Contains(SendIntervalConfigKey))
            {
                var desiredInterval = (int)desiredProperties[SendIntervalConfigKey];
                Console.WriteLine("Updating Send Interval to " + desiredInterval.ToString());
                messageDelay = TimeSpan.FromMilliseconds((int)desiredProperties[SendIntervalConfigKey]);
            }

            if (desiredProperties.Contains(EventCountConfigKey))
            {
                var desiredCount = (int)desiredProperties[EventCountConfigKey];
                Console.WriteLine("Updating Event Count to " + desiredCount.ToString());
                eventCount = (int)desiredProperties[EventCountConfigKey];
            }

            if (desiredProperties.Contains(SendDataConfigKey))
            {
                bool desiredSendDataValue = (bool)desiredProperties[SendDataConfigKey];
                if (desiredSendDataValue != sendData && !desiredSendDataValue)
                {
                    Console.WriteLine("Turning off Send Data. Change twin configuration to start sending again.");
                }

                sendData = desiredSendDataValue;
            }

            var settings = new TwinCollection();
            settings["SendData"] = sendData.ToString().ToLower();
            settings["SendInterval"] = messageDelay.TotalSeconds;
            settings["EventCount"] = eventCount.ToString();
            reportedProperties["settings"] = settings;
            await moduleClient.UpdateReportedPropertiesAsync(reportedProperties);
        }
    }

    public enum ControlCommandEnum
    {
        Reset = 0,
        NoOperation = 1
    }

    class ControlCommand
    {
        [JsonProperty("command")]
        public ControlCommandEnum Command { get; set; }
    }
}
