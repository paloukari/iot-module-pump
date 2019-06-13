# escape=`
FROM mcr.microsoft.com/dotnet/framework/sdk:4.7.2-windowsservercore-ltsc2019 AS builder
WORKDIR C:\src\edge-module

COPY . ./

# COPY PumpSimulator.sln .
# COPY Microsoft.Azure.Devices.Edge.ModuleUtil Microsoft.Azure.Devices.Edge.ModuleUtil
# COPY Microsoft.Azure.Devices.Edge.Util Microsoft.Azure.Devices.Edge.Util
# COPY PumpSimulator PumpSimulator

RUN nuget restore
RUN MSBuild.exe PumpSimulator.sln /t:restore
RUN MSBuild.exe PumpSimulator.sln /t:build /p:Configuration=Release /p:OutputPath=C:\out

# app image
FROM mcr.microsoft.com/dotnet/framework/runtime:4.7.2-windowsservercore-ltsc2019
WORKDIR C:\edge-module  
COPY --from=builder C:\out .  
ENTRYPOINT ["PumpSimulator.exe"] 