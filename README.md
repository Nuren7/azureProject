# Telemetry Pipeline

A .NET 8 Azure Functions app using the isolated worker model. Locally, the HTTP ingestion function places validated events on an Azurite queue; a queue-triggered function enriches each event and appends it as one JSON object per line to `local-database.json` in the project root. Terraform configuration for provisioning the Azure production infrastructure is in [`terraform/`](./terraform/).

## Prerequisites

- .NET 8 SDK
- Azure Functions Core Tools v4
- Azurite, installed with `npm install --global azurite` or through the Azurite VS Code extension
- Terraform 1.5 or later and Azure CLI (only needed to provision Azure infrastructure)

On Windows, Core Tools v4 can be installed with the official npm package:

```powershell
npm install --global azure-functions-core-tools@4 --unsafe-perm true
```

No Azure account, cloud resource, or Azure connection string is required. `local.settings.json` configures the Functions host to use Azurite's default local endpoints.

## Run locally

Open a terminal in the project root and start Azurite in one terminal:

```powershell
New-Item -ItemType Directory -Force .azurite | Out-Null
azurite --silent --location .azurite --debug .azurite\debug.log
```

In a second terminal, restore packages and start the Functions host:

```powershell
dotnet restore
func start
```

The host exposes the HTTP function at `http://localhost:7071/api/v1/telemetry`.

## Submit an event

```powershell
$event = @{
  EventId = "evt-001"
  SourceSystem = "sample-device"
  EventType = "temperature"
  PayloadData = @{ value = 21.5; unit = "C" }
  Timestamp = [DateTimeOffset]::UtcNow.ToString("o")
} | ConvertTo-Json -Depth 10

Invoke-RestMethod `
  -Uri "http://localhost:7071/api/v1/telemetry" `
  -Method Post `
  -ContentType "application/json" `
  -Body $event
```

A valid request receives `202 Accepted` with its `EventId`. Missing or invalid fields receive `400 Bad Request`. Once the queue-triggered function processes the event, `local-database.json` contains a new line with the original event fields plus `LogId`, `ProcessedAtUtc`, and `SchemaVersion`.

## Provision Azure infrastructure with Terraform

The [Terraform configuration](./terraform/main.tf) provisions the `rg-cloudscale-prod` resource group, a uniquely named Standard/LRS storage account and `telemetry-queue`, a Linux Consumption (Y1) plan, and a .NET 8 isolated Linux Function App. The Function App's `AzureWebJobsStorage` setting references the provisioned storage connection string. This deployment is optional and is not required to run the app locally with Azurite.

Prerequisites: Terraform 1.5 or later and Azure CLI. Sign in and select the subscription you want to deploy into, then run these commands from the repository root:

```powershell
az login
$env:ARM_SUBSCRIPTION_ID = az account show --query id --output tsv
terraform -chdir=terraform init
terraform -chdir=terraform validate
terraform -chdir=terraform plan
terraform -chdir=terraform apply
terraform -chdir=terraform output -raw function_app_default_hostname
```

The Linux Consumption (Y1) plan is usage-billed; it is not guaranteed to be cost-free and remains subject to Azure's current free grant, regional availability, and pricing. Azure currently plans to retire Linux Consumption after September 30, 2028; consider Flex Consumption for a new long-lived deployment.

Terraform state contains the storage account key and connection string used by the Function App. Keep the state private and use a protected remote backend for team or production use. This configuration provisions infrastructure only; it does not publish the Function App code. Also, `local-database.json` is local-instance storage, not durable or shared Azure storage—replace that file-based persistence with an Azure database or blob solution before relying on production data.
