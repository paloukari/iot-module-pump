using Newtonsoft.Json;
using System;

namespace SimulatedTemperatureSensor
{
    public class MessageBody
    {
        [JsonProperty(PropertyName = "machine")]
        public Machine Machine { get; set; }

        [JsonProperty(PropertyName = "ambient")]
        public Ambient Ambient { get; set; }

        [JsonProperty(PropertyName = "timeCreated")]
        public DateTime TimeCreated { get; set; }
    }

    public class Machine
    {
        [JsonProperty(PropertyName = "temperature")]
        public double Temperature { get; set; }

        [JsonProperty(PropertyName = "pressure")]
        public double Pressure { get; set; }
    }

    public class Ambient
    {
        [JsonProperty(PropertyName = "temperature")]
        public double Temperature { get; set; }

        [JsonProperty(PropertyName = "humidity")]
        public int Humidity { get; set; }
    }
}
