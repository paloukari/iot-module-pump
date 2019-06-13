# Pump Module

This is a modified simulated pump module to test the ability to deploy a module to an IoTEdge Windows OS with the module compiled using the dotnet framework instead of dotnet core.

- Console Application:  __SimulatedTemperatureSensor__
    - Target Framework: _.NET Framework 4.7.2_

- Class Library: __Microsoft.Azure.Devices.Edge.ModuleUtil__
    - Target Framework: _.NET Standard 2.0_

- Class Library: __Microsoft.Azure.Devices.Edge.Util__
    - Target Framework: _.NET Standard 2.0_

## Install and Start the IoT Edge Runtime

Install the Azure IoT Edge runtime on your IoT Edge device and configure it with a device connection string. 

### Connect to your IoT Edge

The steps in this section all take place on the IoT Edge device, so connect to that device via remote desktop.

### Prepare the device for containers

The installation script automatically installs the Moby engine on your device before installing IoT Edge. Prepare your device by turning on the Containers feature.

```powershell
Install-WindowsFeature -Name Containers -IncludeAllSubFeature
Restart-Computer
```

### Download and install the IoT Edge Service

Use PowerShell to download and install the IoT Edge runtime. When prompted for a DeviceConnectionString, provide the connection string of the Edge Device configured in the IoT Hub.

```powershell
. {Invoke-WebRequest -useb aka.ms/iotedge-win} | Invoke-Expression; `
Install-SecurityDaemon -Manual -ContainerOs Windows

Get-Service iotedge
iotedge list
```

### Disable process identification

> Microsoft.Azure.Devices.Client currently does not support the process identification security feature when compiled against dotnet framework. Be aware this is not a good practice for a production system.  

To disable process identification on your IoT Edge device, you'll need to provide the IP address and port for workload_uri and management_uri in the connect section of the IoT Edge daemon configuration.

Get the IP address first. Enter `ipconfig` in your command line and copy the IP address of the interface.

Edit and update the IoT Edge daemon configuration file `C:\ProgramData\iotedge\config.yaml`

```yaml
connect:
  management_uri: "http://10.0.1.4:15580"
  workload_uri: "http://10.0.1.4:15581"

listen:
  management_uri: "http://10.0.1.4:15580"
  workload_uri: "http://10.0.1.4:15581"
```

Disable the firewall (**Temporary Fix)

```powershell
## Inbound Ports required 15580, 15581
New-NetFirewallRule -DisplayName "IoT Edge" -Direction Inbound -LocalPort 15580,15581 -Protocol TCP -Action Allow

## Outbound Ports required 443, 8883, 5671
New-NetFirewallRule -DisplayName "IoT Edge" -Direction Outbound -LocalPort 443,8883,5671 -Protocol TCP -Action Allow
```

Create an environment variable IOTEDGE_HOST with the management_uri address to allow the iotedge cli to connect to the new management endpoint and then restart the iotedge service.

```powershell
[Environment]::SetEnvironmentVariable("IOTEDGE_HOST", "http://10.0.1.4:15580")

stop-service iotedge
start-service iotedge
get-service iotedge

iotedge list
```

## Build and Push the Module to a Registry

The module needs to be built on Windows System running Windows Containers.

```bash
# Login to the Registry
az acr login --name <registry_name>

# Build the module with ACR
cd modules
az acr run --registry <registry_name> --platform windows --file build.yaml .

# Build the module with Docker
cd modules/PumpModule
docker build -t simulated-pump-win .
```

## Deploy the Module to the Edge Device

> Modify the module image name in the manifest file as necessary based on the __registry__ used.

```json
"SimulatedPump": {
    "settings": {
        "image": "<your_registry>/pump-sensor:2.1",
        "createOptions": ""
    },
    "type": "docker",
    "status": "running",
    "restartPolicy": "always",
    "version": "1.0"
}
```

```powershell
$Device = "<your_edge_device>"
$Hub = "<your_hub>"

# Deploy the Module
#----------------------------------
az iot edge set-modules `
    --device-id $Device `
    --hub-name $Hub `
    --content manifest.json
```

To utilize docker client on the windows server the docker host must be set properly for Moby.

```powershell
[Environment]::SetEnvironmentVariable("DOCKER_HOST", "npipe:////./pipe/iotedge_moby_engine")
```


## Example Message Format

```json
[
    {
        "asset": "whidbey",
        "source": "Simulator",
        "events": [
            {
                "deviceId": "pump_simulator_01",
                "timeStamp": "2019-04-26T14:36:12.0218344Z",
                "machineTemperature": {
                    "value": 22.971214394420951,
                    "units": "degC",
                    "status": 200
                },
                "machinePressure": {
                    "value": 1.2245687284783362,
                    "units": "psig",
                    "status": 200
                },
                "ambientTemperature": {
                    "value": 21.248441741218997,
                    "units": "degC",
                    "status": 200
                },
                "ambientHumdity": {
                    "value": 26.0,
                    "units": "perc",
                    "status": 200
                }
            }
        ]
    }
]
```
