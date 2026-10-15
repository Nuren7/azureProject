terraform {
  required_version = ">= 1.5.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 4.0.0, < 5.0.0"
    }
    random = {
      source  = "hashicorp/random"
      version = ">= 3.6.0, < 4.0.0"
    }
  }
}

provider "azurerm" {
  features {}
}

variable "location" {
  description = "Azure region in which to deploy the telemetry pipeline."
  type        = string
  default     = "westeurope"
}

resource "random_string" "resource_suffix" {
  length  = 8
  lower   = true
  upper   = false
  numeric = true
  special = false
}

resource "azurerm_resource_group" "production" {
  name     = "rg-cloudscale-prod"
  location = var.location

  tags = {
    environment = "production"
    managed_by  = "terraform"
  }
}

resource "azurerm_storage_account" "telemetry" {
  name                     = "stcloudscale${random_string.resource_suffix.result}"
  resource_group_name      = azurerm_resource_group.production.name
  location                 = azurerm_resource_group.production.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"

  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false

  tags = azurerm_resource_group.production.tags
}

resource "azurerm_storage_queue" "telemetry" {
  name               = "telemetry-queue"
  storage_account_id = azurerm_storage_account.telemetry.id
}

resource "azurerm_service_plan" "functions" {
  name                = "asp-cloudscale-prod"
  resource_group_name = azurerm_resource_group.production.name
  location            = azurerm_resource_group.production.location
  os_type             = "Linux"
  sku_name            = "Y1"

  tags = azurerm_resource_group.production.tags
}

resource "azurerm_linux_function_app" "telemetry" {
  name                = "func-cloudscale-prod-${random_string.resource_suffix.result}"
  resource_group_name = azurerm_resource_group.production.name
  location            = azurerm_resource_group.production.location
  service_plan_id     = azurerm_service_plan.functions.id

  storage_account_name       = azurerm_storage_account.telemetry.name
  storage_account_access_key = azurerm_storage_account.telemetry.primary_access_key
  functions_extension_version = "~4"
  https_only                  = true

  app_settings = {
    AzureWebJobsStorage      = azurerm_storage_account.telemetry.primary_connection_string
    FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"
    WEBSITE_RUN_FROM_PACKAGE = "1"
  }

  site_config {
    application_stack {
      dotnet_version                 = "8.0"
      use_dotnet_isolated_runtime    = true
    }
  }

  tags = azurerm_resource_group.production.tags
}

output "function_app_default_hostname" {
  description = "Default HTTPS hostname of the deployed telemetry Function App."
  value       = azurerm_linux_function_app.telemetry.default_hostname
}
