#!/usr/bin/env bash
#
#  Purpose: Initialize the environment
#  Usage:
#    deploy.sh

###############################
## ARGUMENT INPUT            ##
###############################
usage() { echo "Usage: deploy.sh <iot_hub> <device>" 1>&2; exit 1; }

if [ ! -z $1 ]; then IOT_HUB=$1; fi
if [ -z $IOT_HUB ]; then
  usage
fi

if [ ! -z $2 ]; then EDGE_VM=$2; fi
if [ -z $EDGE_VM ]; then
  usage
fi

echo "Deploying modules to ${EDGE_VM}"

az iot edge set-modules \
  --device-id ${EDGE_VM} \
  --hub-name ${IOT_HUB} \
  --content config/deployment.windows-amd64.json