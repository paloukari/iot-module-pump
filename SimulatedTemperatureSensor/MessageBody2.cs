using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace SimulatedTemperatureSensor
{
    public class MessageBody
    {
        [JsonProperty(PropertyName = "asset")]
        public String Asset { get; set; }

        [JsonProperty(PropertyName = "source")]
        public String Source { get; set; }

        [JsonProperty(PropertyName = "events")]
        public List<MessageEvent> Events { get; set; }

    }

    public class MessageEvent
    {
        [JsonProperty(PropertyName = "deviceId")]
        public string DeviceId { get; set; }

        [JsonProperty(PropertyName = "timeStamp")]
        public DateTime TimeStamp { get; set; }

        [JsonProperty(PropertyName = "machineTemperature")]
        public SensorReading MachineTemperature { get; set; }

        [JsonProperty(PropertyName = "machinePressure")]
        public SensorReading MachinePressure { get; set; }

        [JsonProperty(PropertyName = "ambientTemperature")]
        public SensorReading AmbientTemperature { get; set; }

        [JsonProperty(PropertyName = "ambientHumdity")]
        public SensorReading AmbientHumdity { get; set; }
    }

    public class SensorReading
    {
        [JsonProperty(PropertyName = "value")]
        public double Value { get; set; }

        [JsonProperty(PropertyName = "units")]
        public string Units { get; set; }

        [JsonProperty(PropertyName = "status")]
        public int Status { get; set; }
    }
}
