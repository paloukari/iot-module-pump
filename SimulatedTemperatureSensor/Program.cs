using Microsoft.ApplicationInsights;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Edge.ModuleUtil;
using Microsoft.Azure.Devices.Edge.Util;
using Microsoft.Azure.Devices.Edge.Util.Concurrency;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SimulatedTemperatureSensor
{
    class Program
    {
        const string MessageCountConfigKey = "MessageCount";
        const string SendDataConfigKey = "SendData";
        const string SendIntervalConfigKey = "SendInterval";

        static readonly Guid BatchId = Guid.NewGuid();
        static readonly AtomicBoolean Reset = new AtomicBoolean(false);
        static readonly Random Rnd = new Random();
        static bool sendData = true;

        static TimeSpan messageDelay;

        static TelemetryClient telemetryClient;
        static bool insights = false;

        public static int Main() => MainAsync().Result;

        static async Task<int> MainAsync()
        {
            Console.WriteLine("SimulatedTemperatureSensor Main() started.");
            var appSettings = ConfigurationManager.AppSettings;

            // Setup App Insights
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")))
            {
                Console.WriteLine("Application Insights Enabled.");
                insights = true;
                telemetryClient = new TelemetryClient();
                telemetryClient.Context.Device.Id = Environment.MachineName;
                telemetryClient.TrackEvent("SimulatedTemperatureSensor started");
                telemetryClient.GetMetric("SimulatorCount").TrackValue(1);  
            }


            if (!TimeSpan.TryParse(appSettings["MessageDelay"], out messageDelay))
            {
                messageDelay = TimeSpan.FromSeconds(5);
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
            if (currentTwinProperties.Properties.Desired.Contains(SendIntervalConfigKey))
            {
                messageDelay = TimeSpan.FromSeconds((int)currentTwinProperties.Properties.Desired[SendIntervalConfigKey]);
            }

            if (currentTwinProperties.Properties.Desired.Contains(SendDataConfigKey))
            {
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
                    events.Add(new MessageEvent
                    {
                        DeviceId = Environment.GetEnvironmentVariable("DEVICE") ?? Environment.MachineName,
                        TimeStamp = DateTime.UtcNow,
                        Temperature = new SensorReading
                        {
                            Value = currentTemp,
                            Units = "degC",
                            Status = 192
                        },
                        Pressure = new SensorReading
                        {
                            Value = sim.PressureMin + ((currentTemp - sim.TempMin) * normal),
                            Units = "psig",
                            Status = 192
                        },
                        SuctionPressure = new SensorReading
                        {
                            Value = sim.PressureMin + 4 + ((currentTemp - sim.TempMin) * normal),
                            Units = "psig",
                            Status = 192
                        },
                        DischargePressure = new SensorReading
                        {
                            Value = sim.PressureMin + 1 + ((currentTemp - sim.TempMin) * normal),
                            Units = "psig",
                            Status = 192
                        },
                        Flow = new SensorReading
                        {
                            Value = Rnd.Next(78, 82),
                            Units = "perc",
                            Status = 192,
                            Misc = "GOOD"
                        }
                    });

                    var tempData = new MessageBody
                    {
                        Asset = Environment.GetEnvironmentVariable("ASSET") ?? "PoC",
                        Source = "Simulator",
                        Events = events
                    };

                    string dataBuffer = JsonConvert.SerializeObject(tempData);
                    var eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));
                    eventMessage.Properties.Add("sequenceNumber", count.ToString());
                    eventMessage.Properties.Add("batchId", BatchId.ToString());
                    eventMessage.Properties.Add("asset", tempData.Asset);
                    Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Body: [{dataBuffer}]");

                    await moduleClient.SendEventAsync("temperatureOutput", eventMessage);
                    if (insights) telemetryClient.GetMetric("SendEvent").TrackValue(1);
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
            // At this point just update the configure configuration.
            if (desiredPropertiesPatch.Contains(SendIntervalConfigKey))
            {
                messageDelay = TimeSpan.FromSeconds((int)desiredPropertiesPatch[SendIntervalConfigKey]);
            }

            if (desiredPropertiesPatch.Contains(SendDataConfigKey))
            {
                bool desiredSendDataValue = (bool)desiredPropertiesPatch[SendDataConfigKey];
                if (desiredSendDataValue != sendData && !desiredSendDataValue)
                {
                    Console.WriteLine("Sending data disabled. Change twin configuration to start sending again.");
                }

                sendData = desiredSendDataValue;
            }

            var moduleClient = (ModuleClient)userContext;
            var patch = new TwinCollection($"{{ \"SendData\":{sendData.ToString().ToLower()}, \"SendInterval\": {messageDelay.TotalSeconds}}}");
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
