# AltaworxMobilityConfigurationChange Lambda Function - Method Flow Documentation

## Overview
This document outlines the sequential method flow for the AltaworxMobilityConfigurationChange Lambda function, showing how methods navigate from start to end across multiple C# files.

## Entry Point and Main Flow

### 1. Lambda Entry Point
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)`
- **Line:** 43
- **Purpose:** Main entry point for the Lambda function

### 2. Base Function Initialization
**File:** `AWSFunctionBase.cs`
**Method:** `BaseFunctionHandler(ILambdaContext context, bool skipOUSpecificLogic = false)`
- **Line:** 40
- **Called from:** `FunctionHandler` → Line 48
- **Purpose:** Initializes KeySysLambdaContext for logging and database connections

## Message Processing Flow

### 3. SQS Message Processing Loop
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `FunctionHandler` (continuation)
- **Lines:** 67-125
- **Purpose:** Iterates through SQS event records and processes each message

### 4. Get Mobility Configuration Change Details
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `GetMobilityConfigurationChangeDetails(KeySysLambdaContext context, int id)`
- **Line:** 206
- **Called from:** `FunctionHandler` → Line 85
- **Purpose:** Retrieves mobility configuration change details from database

#### 4.1 Repository Data Access
**File:** `MobilityConfigurationChangeRepository.cs`
**Method:** `GetUnprocessedChange(int changeId)`
- **Line:** 40
- **Called from:** `GetMobilityConfigurationChangeDetails` → Line 211
- **Purpose:** Executes stored procedure `usp_Get_MobilityConfigurationChangeDetails`

#### 4.2 Data Reader Method
**File:** `MobilityConfigurationChangeRepository.cs`
**Method:** `ReadMobilityConfigurationChange(IDataRecord reader)`
- **Line:** 99
- **Called from:** `GetUnprocessedChange` → Line 55
- **Purpose:** Maps database reader to MobilityConfigurationChange object

### 5. Service Provider Lookup
**File:** `ServiceProviderCommon.cs`
**Method:** `GetServiceProvider(string connectionString, int serviceProviderId)`
- **Line:** 45
- **Called from:** `FunctionHandler` → Line 92
- **Purpose:** Retrieves service provider details and integration type

## Integration Type Branching

### 6A. eBonding Integration Path
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `EnqueueEbondingChangeAsync(KeySysLambdaContext context, IDictionary<string, string> settings, int changeId)`
- **Line:** 274
- **Called from:** `FunctionHandler` → Line 99 (if IntegrationType.eBonding)
- **Purpose:** Enqueues message to eBonding care order request queue

### 6B. Telegence Integration Path
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `ProcessMobilityConfigurationChange(...)`
- **Line:** 135
- **Called from:** `FunctionHandler` → Line 113 (if not eBonding)
- **Purpose:** Processes mobility configuration changes via Telegence API

## Telegence Processing Flow

### 7. Get Telegence Authentication
**File:** `TelegenceCommon.cs`
**Method:** `GetTelegenceAuthenticationInformation(string connectionString, int serviceProviderId)`
- **Line:** 25
- **Called from:** `ProcessMobilityConfigurationChange` → Line 142
- **Purpose:** Retrieves Telegence authentication credentials from database

### 8. Generate Telegence Request
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `GenerateTelegenceMobilityConfigurationRequest(MobilityConfigurationChangeDetails details)`
- **Line:** 166
- **Called from:** `ProcessMobilityConfigurationChange` → Line 145
- **Purpose:** Creates TelegenceMobilityConfigurationRequest object with service characteristics

### 9. Update Telegence Mobility Configuration
**File:** `TelegenceCommon.cs`
**Method:** `UpdateTelegenceMobilityConfiguration(...)`
- **Line:** 274
- **Called from:** `ProcessMobilityConfigurationChange` → Line 149
- **Purpose:** Makes HTTP PATCH request to Telegence API to update mobility configuration

## Post-Processing Flow

### 10. Mark as Processed
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `MarkProcessed(KeySysLambdaContext context, MobilityConfigurationChangeDetails details, int id)`
- **Line:** 220
- **Called from:** `ProcessMobilityConfigurationChange` → Line 156 (if successful)
- **Purpose:** Updates database to mark configuration change as processed

#### 10.1 Rate Plan Change Processing
**File:** `MobilityConfigurationChangeRepository.cs`
**Method:** `CompleteRatePlanChange(...)`
- **Line:** 65
- **Called from:** `MarkProcessed` → Line 228 (if ConfigurationType == "RatePlan")
- **Purpose:** Executes stored procedures to complete rate plan changes

#### 10.2 Mobility Feature Processing
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `UpdateMobilityDeviceFeature(KeySysLambdaContext context, string SOCCodes, int deviceId, int changeId)`
- **Line:** 250
- **Called from:** `MarkProcessed` → Line 241 (if ConfigurationType == "MobilityFeature")
- **Purpose:** Updates mobility device features in database

### 11. Send Response to AMOP 2.0 (Non-Production Only)
**File:** `AltaworxMobilityConfigurationChange.cs`
**Method:** `SendResponseToAMOP20(KeySysLambdaContext context, string jobName, string lambdaResponse)`
- **Line:** 306
- **Called from:** `ProcessMobilityConfigurationChange` → Line 162 (if not production)
- **Purpose:** Sends response back to AMOP 2.0 system

## Cleanup and Completion

### 12. Cleanup Resources
**File:** `AWSFunctionBase.cs`
**Method:** `CleanUp(KeySysLambdaContext context)`
- **Line:** 53
- **Called from:** `FunctionHandler` → Line 132
- **Purpose:** Cleans up resources and finalizes logging

## Method Flow Summary

```
FunctionHandler (Entry Point)
    ↓
BaseFunctionHandler (Context Initialization)
    ↓
GetMobilityConfigurationChangeDetails
    ↓
MobilityConfigurationChangeRepository.GetUnprocessedChange
    ↓
ReadMobilityConfigurationChange
    ↓
ServiceProviderCommon.GetServiceProvider
    ↓
[Branch: eBonding]                    [Branch: Telegence]
EnqueueEbondingChangeAsync     →      ProcessMobilityConfigurationChange
                                          ↓
                                   GetTelegenceAuthenticationInformation
                                          ↓
                                   GenerateTelegenceMobilityConfigurationRequest
                                          ↓
                                   UpdateTelegenceMobilityConfiguration
                                          ↓
                                   MarkProcessed
                                          ↓
                                   [If RatePlan] CompleteRatePlanChange
                                   [If MobilityFeature] UpdateMobilityDeviceFeature
                                          ↓
                                   SendResponseToAMOP20 (Non-Production)
    ↓
CleanUp (Final Cleanup)
```

## Key Database Operations

1. **usp_Get_MobilityConfigurationChangeDetails** - Retrieves configuration change details
2. **usp_Telegence_Get_AuthenticationByProviderId** - Gets Telegence authentication
3. **UPDATE_MOBILITY_CONFIGURATION_CHANGE_DETAILS** - Marks change as processed
4. **UPDATE_MOBILITY_RATE_PLAN_DEVICE_TABLES** - Updates rate plan in device tables
5. **usp_Update_MobilityDeviceFeature** - Updates mobility device features

## Key External API Calls

1. **Telegence Mobility Configuration API** - PATCH request to update subscriber configuration
2. **AWS SQS** - Enqueues messages for eBonding processing
3. **AMOP 2.0 Migration Job Scheduler** - Sends response in non-production environments

## Error Handling

Error handling is implemented at multiple levels:
- Try-catch blocks in main methods
- Retry policies in TelegenceCommon methods
- Logging throughout the execution flow
- Graceful handling of missing data and failed API calls