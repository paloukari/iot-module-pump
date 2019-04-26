# SimulatedTemperatureSensor

This is a modified simulated temperature sensor to test the ability to deploy a module to an IoTEdge Windows OS with the module compiled using the dotnet framework instead of dotnet core.

## Build and Deploy the Module to a Registry

The module needs to be built on Windows System running Windows Containers.

```powershell
# Setup the Environment Variables
#----------------------------------
$Env:Registry = "danielscholl"
$Env:Version = "1.0"

# Build and Push the Module Image
#----------------------------------
docker build -t $Env:Registry/simulated-win-sensor:$Env:Version .
docker push $Env:Registry/simulated-win-sensor:$Env:Version
```

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

### Disable process identification

> Microsoft.Azure.Devices.Client currently does not support the process identification security feature when compiled against dotnet framework. This however is not a good practice for use in production.  

To disable process identification on your IoT Edge device, you'll need to provide the IP address and port for workload_uri and management_uri in the connect section of the IoT Edge daemon configuration.

Get the IP address first. Enter `ipconfig` in your command line and copy the IP address of the interface.

Edit and update the IoT Edge daemon configuration file `C:\ProgramData\iotedge\config.yaml`

```yaml
connect:
  management_uri: "http://192.168.1.159:15580"
  workload_uri: "http://192.168.1.159:15581"

listen:
  management_uri: "http://192.168.1.159:15580"
  workload_uri: "http://192.168.1.159:15581"
```

Create an environment variable IOTEDGE_HOST with the management_uri address to allow the iotedge cli to connect to the new management endpoint and then restart the iotedge service.

```powershell
[Environment]::SetEnvironmentVariable("IOTEDGE_HOST", "http://192.168.1.159:15580")

stop-service iotedge
start-service iotedge
get-service iotedge

iotedge list
```

## Deploy the Module to the Edge Device

Modify the module image in the manifest file as necessary based on the module image name used.

```powershell
# Setup the Environment Variables
#----------------------------------
$Env:Device = "edge-windows"
$Env:Hub = "danielscholl"

# Deploy the Module
#----------------------------------
az iot edge set-modules `
    --device-id $Env:Device `
    --hub-name $Env:Hub `
    --content manifest.json
```
