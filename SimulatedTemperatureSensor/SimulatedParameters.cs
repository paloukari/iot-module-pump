using System.Configuration;

namespace SimulatedTemperatureSensor
{
    class SimulatorParameters
    {
        public double MachineTempMin { get; set; }

        public double MachineTempMax { get; set; }

        public double MachinePressureMin { get; set; }

        public double MachinePressureMax { get; set; }

        public double AmbientTemp { get; set; }

        public int HumidityPercent { get; set; }

        public static SimulatorParameters Create()
        {
            var appSettings = ConfigurationManager.AppSettings;

            double machineTempMin;
            if (!double.TryParse(appSettings["machineTempMin"], out machineTempMin))
            {
                machineTempMin = 21;
            }

            double machineTempMax;
            if (!double.TryParse(appSettings["machineTempMax"], out machineTempMax))
            {
                machineTempMax = 100;
            }

            double machinePressureMin;
            if (!double.TryParse(appSettings["machinePressureMin"], out machinePressureMin))
            {
                machinePressureMin = 1;
            }

            double machinePressureMax;
            if (!double.TryParse(appSettings["machinePressureMax"], out machinePressureMax))
            {
                machinePressureMax = 10;
            }

            double ambientTemp;
            if (!double.TryParse(appSettings["ambientTemp"], out ambientTemp))
            {
                ambientTemp = 21;
            }

            int HumidityPercent;
            if (!int.TryParse(appSettings["ambientHumidity"], out HumidityPercent))
            {
                HumidityPercent = 25;
            }

            return new SimulatorParameters
            {
                MachineTempMin = machineTempMin,
                MachineTempMax = machineTempMax,
                MachinePressureMin = machinePressureMin,
                MachinePressureMax = machinePressureMax,
                AmbientTemp = ambientTemp,
                HumidityPercent = HumidityPercent
            };
        }
    }
}
