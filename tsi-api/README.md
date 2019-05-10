# Executing Time Series Insights API

__PreRequisites__

Requires the use of [VS Code](https://code.visualstudio.com/)
Requires the use of the extension [Rest Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client)

## Create a Service Principal

```powershell
$SP_NAME = "TSI-API"
az ad sp create-for-rbac --name $SP_NAME

# Expected Result
{
  "appId": "00000000-0000-0000-0000-000000000000",
  "displayName": "Terraform-Principal",
  "name": "http://Terraform-Principal",
  "password": "0000-0000-0000-0000-000000000000",
  "tenant": "00000000-0000-0000-0000-000000000000"
}
```

## Grant access for the Service Principal

[Provide access](https://docs.microsoft.com/en-us/azure/time-series-insights/time-series-insights-data-access) to the Service Principal for the Time Series Environment.

## Edit VS Code Settings File to store local variables

```json
"rest-client.environmentVariables": {
  "local": {
    "TENANT_ID": "<tenant>",
    "CLIENT_ID": "<appId>",
    "CLIENT_SECRET": "<password>"
  }
}
```

## Send the requests in the desired {file}.http

1. model.http  _(Examples for creating Types and Hiearchies)_