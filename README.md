# Pump Module

This is a modified simulated pump module to test the ability to deploy a module to an IoTEdge Windows OS with the module compiled using the dotnet framework instead of dotnet core.

- Console Application:  __SimulatedTemperatureSensor__
    - Target Framework: _.NET Framework 4.7.2_

- Class Library: __Microsoft.Azure.Devices.Edge.ModuleUtil__
    - Target Framework: _.NET Standard 2.0_

- Class Library: __Microsoft.Azure.Devices.Edge.Util__
    - Target Framework: _.NET Standard 2.0_


### Build and Push the Module to a Registry

The module needs to be built on Windows System running Windows Containers.

```bash
# Login to the Registry
az acr login --name <registry_name>

# Build the module with ACR
cd modules
az acr run --registry <registry_name> --platform windows --file build.yaml .
```

### Deploy the Module to the Edge Device

> Modify the module image name in the manifest file as necessary based on the __registry__ used.

```json
"SimulatedPump": {
    "settings": {
        "image": "<your_registry>/module-pump-win:latest",
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


### Example Message Format

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
