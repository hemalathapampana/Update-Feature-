# Mobility Configuration Change Flow Breakdown

This document provides a detailed breakdown of each method in the mobility configuration change processing workflow. Each section includes 3-4 key points explaining what happens when the method is called.

## 1. FunctionHandler (Entry Point)

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 43-133

**What it does:** The main entry point that receives and initiates the mobility configuration change request

- **Initializes Context:** Calls `BaseFunctionHandler(context)` to set up the execution environment and logging infrastructure
- **Loads Environment Settings:** Retrieves configuration URLs for mobility line configuration, proxy settings, and eBonding queue URLs from environment variables
- **Processes SQS Records:** Iterates through each SQS event record, extracting the `MobilityLineConfigurationQueueId` from message attributes
- **Orchestrates Processing:** For each valid queue ID, calls `GetMobilityConfigurationChangeDetails()` and routes to appropriate service provider processing

## 2. BaseFunctionHandler (Context Initialization)

**Location:** `AWSFunctionBase.cs` - lines 40-45

**What it does:** Sets up the execution context and prepares the environment for processing

- **Creates Lambda Context:** Instantiates a new `KeySysLambdaContext` object with production/non-production environment detection
- **Establishes Database Connections:** Sets up central database connection strings and authentication credentials
- **Initializes Logging:** Configures structured logging capabilities with caller information tracking
- **Loads OU Settings:** Optionally loads Organizational Unit specific settings unless `skipOUSpecificLogic` is true

## 3. GetMobilityConfigurationChangeDetails

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 206-218

**What it does:** Retrieves the specific details of the mobility configuration change request

- **Creates Repository Instance:** Instantiates `MobilityConfigurationChangeRepository` with the central database connection string
- **Calls GetUnprocessedChange:** Delegates to the repository method to fetch unprocessed mobility configuration change by queue ID
- **Handles Exceptions:** Catches and logs any database or connection exceptions, returning null on failure
- **Returns Change Object:** Returns a `MobilityConfigurationChange` object containing all relevant change details or null if not found

## 4. MobilityConfigurationChangeRepository.GetUnprocessedChange

**Location:** `MobilityConfigurationChangeRepository.cs` - lines 40-63

**What it does:** Queries the database to find mobility configuration changes that haven't been processed yet

- **Executes Stored Procedure:** Calls `usp_Get_MobilityConfigurationChangeDetails` with the provided queue ID parameter
- **Implements Retry Policy:** Uses Polly retry mechanism to handle transient database connection failures
- **Reads Database Record:** If a record is found, calls `ReadMobilityConfigurationChange()` to map the data reader to an object
- **Returns Result:** Returns a populated `MobilityConfigurationChange` object or null if no unprocessed change is found

## 5. ReadMobilityConfigurationChange

**Location:** `MobilityConfigurationChangeRepository.cs` - lines 99-110

**What it does:** Reads the full details of the unprocessed mobility configuration change

- **Maps Database Fields:** Creates a new `MobilityConfigurationChange` object from the data reader, mapping MobilityDeviceId, SubscriberNumber, and ServiceProviderId
- **Handles JSON Details:** Extracts the `MobilityConfigurationChangeDetails` field as a JSON string for later deserialization
- **Processes Optional Fields:** Safely handles nullable TenantId field, setting it to null if DBNull.Value
- **Sets Processing Status:** Maps the IsProcessed boolean flag to indicate current processing state

## 6. ServiceProviderCommon.GetServiceProvider

**Location:** `ServiceProviderCommon.cs` - lines 45-82

**What it does:** Determines which service provider (eBonding or Telegence) should handle this change

- **Executes SQL Query:** Runs a direct SQL query against the ServiceProvider table to fetch provider details by ID
- **Maps Provider Properties:** Creates a ServiceProvider object with Id, Name, DisplayName, IntegrationId, and various configuration settings
- **Determines Integration Type:** Returns the IntegrationId which determines whether to use eBonding or Telegence processing branch
- **Includes Write Permissions:** Maps WriteIsEnabled flag to determine if the service provider allows write operations

---

## Branch Processing

### eBonding Branch

#### EnqueueEbondingChangeAsync

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 274-304

**What it does:** Adds the mobility configuration change to an eBonding processing queue

- **Validates Queue URL:** Checks if the eBonding care order request queue URL is configured, returns early if not available
- **Creates SQS Client:** Initializes an Amazon SQS client using AWS credentials from the context
- **Builds SQS Message:** Constructs a SendMessageRequest with the changeId as a message attribute and 5-second delay
- **Sends to Queue:** Asynchronously sends the message to the configured SQS queue and logs any HTTP status code errors

### Telegence Branch

#### ProcessMobilityConfigurationChange

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 135-164

**What it does:** Initiates the Telegence-specific processing workflow

- **Retrieves Authentication:** Calls `TelegenceCommon.GetTelegenceAuthenticationInformation()` to get API credentials for the service provider
- **Generates Request Payload:** Creates a Telegence-compatible request object using `GenerateTelegenceMobilityConfigurationRequest()`
- **Executes Update:** Calls `TelegenceCommon.UpdateTelegenceMobilityConfiguration()` with authentication, request payload, and endpoint URLs
- **Handles Result:** Marks the change as processed if successful, and sends response to AMOP20 in non-production environments

#### GetTelegenceAuthenticationInformation

**Location:** `TelegenceCommon.cs` - lines 25-67

**What it does:** Retrieves authentication credentials and tokens needed for Telegence API calls

- **Executes Stored Procedure:** Calls `usp_Telegence_Get_AuthenticationByProviderId` to fetch authentication details from database
- **Maps Authentication Object:** Creates `TelegenceAuthentication` object with ClientId, ClientSecret, production/sandbox URLs, and username/password
- **Handles Environment URLs:** Sets both ProductionUrl and SandboxUrl for environment-specific API endpoint selection
- **Includes Write Permissions:** Maps WriteIsEnabled flag and BillPeriodEndDay for processing validation

#### GenerateTelegenceMobilityConfigurationRequest

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 166-204

**What it does:** Creates a properly formatted request payload for the Telegence API

- **Creates Service Characteristics:** Builds a list of ServiceCharacteristic objects for mobility configuration IDs to add and remove
- **Handles Configuration Types:** Uses different characteristic names based on ConfigurationType (MobilityFeature vs RatePlan: "addOfferingCode"/"singleUserCode" vs "removeOfferingCode")
- **Sets Effective Date:** Parses and formats the EffectiveDate from the details into ISO 8601 format with "Z" suffix
- **Returns Request Object:** Creates and returns a `TelegenceMobilityConfigurationRequest` with serviceCharacteristic array and effectiveDate

#### UpdateTelegenceMobilityConfiguration

**Location:** `TelegenceCommon.cs` - lines 274-356

**What it does:** Sends the configuration change request to Telegence and processes the response

- **Validates Write Permissions:** Checks if telegenceAuthentication.WriteIsEnabled is true before proceeding with API calls
- **Builds Request URL:** Constructs the mobility configuration URL by appending subscriber number to the endpoint
- **Handles Proxy/Direct Calls:** Routes through proxy service if proxyUrl is provided, otherwise makes direct HTTP PATCH calls to Telegence
- **Returns Success Status:** Returns boolean result based on HTTP response status code and logs any errors encountered

#### MarkProcessed

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 220-248

**What it does:** Updates the database to mark the change as completed

- **Branches by Configuration Type:** Handles RatePlan changes differently from MobilityFeature changes using ConfigurationType property
- **Processes RatePlan Changes:** For RatePlan type, calls `repo.CompleteRatePlanChange()` with new rate plan code and optimization group
- **Handles MobilityFeature Changes:** For MobilityFeature type, calls `UpdateMobilityDeviceFeature()` with current SOC codes and device ID
- **Logs Processing Steps:** Records completion activities and handles any exceptions during the database update process

### Conditional Processing

#### CompleteRatePlanChange

**Location:** `MobilityConfigurationChangeRepository.cs` - lines 65-97

**What it does:** Handles specific rate plan change completion tasks

- **Updates Change Status:** Executes `UPDATE_MOBILITY_CONFIGURATION_CHANGE_DETAILS` stored procedure to mark the change as processed
- **Updates Device Tables:** Calls `UPDATE_MOBILITY_RATE_PLAN_DEVICE_TABLES` stored procedure with new rate plan code and effective date
- **Handles Optional Parameters:** Includes optimization group ID and rate plan effective date only if they have values
- **Implements Retry Logic:** Uses sqlRetryPolicy for both database operations to handle transient failures

#### UpdateMobilityDeviceFeature

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 250-272

**What it does:** Updates device-specific mobility features

- **Executes Stored Procedure:** Calls `dbo.usp_Update_MobilityDeviceFeature` with SOC codes, mobility device ID, and change ID
- **Updates Feature Database:** Modifies both device and Telegence device tables with manual feature changes
- **Sets Processing Metadata:** Includes processedBy parameter indicating the operation was performed by the lambda function
- **Uses Direct SQL Connection:** Creates a direct SqlConnection and SqlCommand for the database operation with timeout handling

#### SendResponseToAMOP20 (Non-Production)

**Location:** `AltaworxMobilityConfigurationChange.cs` - lines 306-326

**What it does:** Sends processing results back to the AMOP20 system (only in non-production environments)

- **Constructs API Request:** Builds a JSON payload with job name, response message, and migration job scheduler path
- **Makes HTTP POST:** Sends the response to the AMOP2.0 migration job scheduler API endpoint using HttpClient
- **Handles Response Status:** Logs success or error messages based on the HTTP response status from the AMOP2.0 system
- **Environment Specific:** Only executes when `context.IsProduction` is false, providing feedback for testing/development

## 7. CleanUp (Final Cleanup)

**Location:** `AWSFunctionBase.cs` - lines 53-56

**What it does:** Performs final cleanup operations, releases resources, and logs completion

- **Delegates to Context:** Calls `context.CleanUp()` method on the KeySysLambdaContext object
- **Releases Resources:** Ensures proper disposal of database connections, HTTP clients, and other managed resources
- **Finalizes Logging:** Completes any pending log entries and flushes log buffers to ensure audit trail completion
- **Memory Management:** Triggers garbage collection hints and cleans up any temporary objects created during processing