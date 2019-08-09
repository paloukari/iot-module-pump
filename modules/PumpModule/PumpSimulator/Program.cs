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
        static TimeSpan messageDelay;
        static int eventCount = 1;

        static TelemetryClient telemetryClient;
        static bool insights = false;
        static bool debug = false;

        public static int Main() => MainAsync().Result;

        static async Task<int> MainAsync()
        {
            try
            {
                Console.WriteLine("PumpSimulator Main() started.");
                var appSettings = ConfigurationManager.AppSettings;

                // Setup App Insights
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")))
                {
                    Console.WriteLine("Application Insights Enabled.");
                    insights = true;
                    telemetryClient = new TelemetryClient();
                    telemetryClient.Context.Device.Id = Environment.MachineName;
                    telemetryClient.TrackEvent("Simulator started");
                    telemetryClient.GetMetric("PumpCount").TrackValue(1);  
                }

                // Setup Debug
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOG_LEVEL")))
                {
                    if(Environment.GetEnvironmentVariable("LOG_LEVEL") == "debug") 
                    {
                        Console.WriteLine("Debug Turned On.");
                        debug = true;
                    }
                }


                if (!TimeSpan.TryParse(appSettings["MessageDelay"], out messageDelay))
                {
                    messageDelay = TimeSpan.FromSeconds(1000);
                }

                int messageCount;

                if(!int.TryParse(Environment.GetEnvironmentVariable(MessageCountConfigKey), out messageCount))
                {
                    if (!int.TryParse(appSettings[MessageCountConfigKey], out messageCount))
                    {
                        messageCount = 500;
                    }
                }

                var simulatorParameters = SimulatorParameters.Create();

                Console.WriteLine(
                    $"Initializing simulated sensor to send {(SendUnlimitedMessages(messageCount) ? "unlimited" : messageCount.ToString())} "
                    + $"messages, at an interval of {messageDelay.TotalSeconds} seconds.\n"
                    + $"To change this, set the environment variable {MessageCountConfigKey} to the number of messages that should be sent (set it to -1 to send unlimited messages).");

                ModuleClient moduleClient = await ModuleUtil.CreateModuleClientAsync(
                        TransportType.Amqp_Tcp_Only,
                        ModuleUtil.DefaultTimeoutErrorDetectionStrategy,
                        ModuleUtil.DefaultTransientRetryStrategy);
                await moduleClient.OpenAsync();
                await moduleClient.SetMethodHandlerAsync("reset", ResetMethod, null);
                

                (CancellationTokenSource cts, ManualResetEventSlim completed, Option<object> handler) = ShutdownHandler.Init(TimeSpan.FromSeconds(5), null);

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

                ModuleClient userContext = moduleClient;
                await moduleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdated, userContext);
                await moduleClient.SetInputMessageHandlerAsync("control", ControlMessageHandle, userContext);
                await SendEvents(moduleClient, messageCount, simulatorParameters, cts);
                await cts.Token.WhenCanceled();


                completed.Set();
                handler.ForEach(h => GC.KeepAlive(h));
                Console.WriteLine("SimulatedTemperatureSensor Main() finished.");
                return 0;
            }
            catch (System.Exception ex)
            {
                Console.WriteLine("PumpSimulator Main() error.");

                var telemetry = new ExceptionTelemetry(ex);
                Type exceptionType = ex.GetType();
                if (exceptionType != null)
                {
                    foreach (PropertyInfo property in exceptionType.GetProperties())
                    {
                        if (string.Equals(property.Name, "StackTrace") ||
                            string.Equals(property.Name, "Message") ||
                            string.Equals(property.Name, "TargetSite"))
                        {
                            // skip duplicate data
                        }
                        else
                        {
                            telemetry.Properties[$"{exceptionType.Name}.{property.Name}"] 
                                = JsonConvert.SerializeObject(property.GetValue(ex), new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
                        }
                    }
                }
                telemetryClient.TrackException(telemetry);

                Console.WriteLine(ex.Message);

                return -1;
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

                    await moduleClient.SendEventAsync("temperatureOutput", eventMessage);
                    if (insights)
                    {
                        telemetryClient.GetMetric("SendMessage").TrackValue(1);
                        telemetryClient.Context.Operation.Name = "Special Operation";
                        Metric sizeStats = telemetryClient.GetMetric("Special Operation Message Size");
                        sizeStats.TrackValue(size);
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

        static async Task OnDesiredPropertiesUpdated(TwinCollection desiredPropertiesPatch, object userContext)
        {
            Console.WriteLine("Device Twin Update Received");

            // At this point just update the configure configuration.
            if (desiredPropertiesPatch.Contains(SendIntervalConfigKey))
            {
                var desiredInterval = (int)desiredPropertiesPatch[SendIntervalConfigKey];
                Console.WriteLine("Updating Send Interval to " + desiredInterval.ToString());
                messageDelay = TimeSpan.FromMilliseconds((int)desiredPropertiesPatch[SendIntervalConfigKey]);
            }

            if (desiredPropertiesPatch.Contains(EventCountConfigKey))
            {
                var desiredCount = (int)desiredPropertiesPatch[EventCountConfigKey];
                Console.WriteLine("Updating Event Count to " + desiredCount.ToString());
                eventCount = (int)desiredPropertiesPatch[EventCountConfigKey];
            }

            if (desiredPropertiesPatch.Contains(SendDataConfigKey))
            {
                bool desiredSendDataValue = (bool)desiredPropertiesPatch[SendDataConfigKey];
                if (desiredSendDataValue != sendData && !desiredSendDataValue)
                {
                    Console.WriteLine("Turning off Send Data. Change twin configuration to start sending again.");
                }

                sendData = desiredSendDataValue;
            }

            var moduleClient = (ModuleClient)userContext;
            var patch = new TwinCollection($"{{ \"SendData\":{sendData.ToString().ToLower()}, \"SendInterval\": {messageDelay.TotalSeconds}, \"EventCount\": {eventCount.ToString()}}}");
            await moduleClient.UpdateReportedPropertiesAsync(patch); // Just report back last desired property.
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
