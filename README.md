# Pump Module

This is a modified simulated pump module to test the ability to deploy a module to an IoTEdge Windows OS with the module compiled using the dotnet framework instead of dotnet core.


- Console Application:  __SimulatedTemperatureSensor__
    - Target Framework: _.NET Framework 4.7.2_

- Class Library: __Microsoft.Azure.Devices.Edge.ModuleUtil__
    - Target Framework: _.NET Standard 2.0_

- Class Library: __Microsoft.Azure.Devices.Edge.Util__
    - Target Framework: _.NET Standard 2.0_


### Build using Azure Pipelines

[![Build Status](https://dascholl.visualstudio.com/IoT/_apis/build/status/danielscholl.iot-module-pump?branchName=master)](https://dascholl.visualstudio.com/IoT/_build/latest?definitionId=38&branchName=master)

The DevOps Pipeline requires a Variable Group to be used as well as a Service Connection Endpoint

```
Required Variables
----------------------------------
ACR_HOST: The FQDN of the Registry
ACR_USER: A login user name of the Registry
ACR_PASSWORD: The password for the login user name of the Registry
ACR_REGISTRY: JSON Snipit for the Registry
    {"loginServer":"{registryName}.azurecr.io", "id" : "/subscriptions/{azureSubscription}/resourceGroups/{resourceGroupName/providers/Microsoft.ContainerRegistry/registries/{registryName"}
SERVICE_ENDPOINT: ServiceEndpoint Connection Name
```

### Manual Build and Push the Module to a Registry

The module can be manually built within the Azure Container Registry itself.

```bash
# Login to the Registry
az acr login --name <registry_name>

# Build the module with ACR
az acr run --registry <registry_name> --platform windows --file build.yaml .
```

>Note:  Build Time est:  14:30 minutes

### Create the Deployment Manifest

Update the .env settings file with the proper values

```
# CONTAINER REGISTRY
ACR_USER=''
ACR_PASSWORD=''
ACR_HOST=''
```

Configure the Deployment Template using VS Code

### Deploy the Module to the Edge Device

Create the Manifest File

> Note:  The module.json file has variables to automatically parse Image Id's ensure the proper Image Id is in the manifest.

```powershell
$Device = "<your_edge_device>"
$Hub = "<your_hub>"

# Deploy the Module
#----------------------------------
az iot edge set-modules `
    --device-id $Device `
    --hub-name $Hub `
    --content config/deployment.windows-amd64.json.json
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
    },
    {
        "asset": "bainbridge",
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
    },
    {
        "asset": "fidalgo",
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
    },
    {
        "asset": "camano",
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
