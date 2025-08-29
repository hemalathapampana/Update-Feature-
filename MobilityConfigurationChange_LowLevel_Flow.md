# Mobility Configuration Change - Low Level Flow Documentation

## Overview
This document provides a detailed low-level flow analysis of the Mobility Configuration Change processing system, breaking down each component and method call with their specific implementations.

## 1. FunctionHandler (Entry Point)

### Method: `FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)`
**Location**: `AltaworxMobilityConfigurationChange.cs:43-133`

#### Low-Level Details:
1. **Context Initialization**
   - Creates `KeySysLambdaContext` instance via `BaseFunctionHandler(context)`
   - Loads environment variables for configuration URLs and queue settings

2. **Environment Settings Loading**
   ```csharp
   var settings = new Dictionary<string, string>
   {
       {MOBILITY_LINE_CONFIGURATION_URL, Environment.GetEnvironmentVariable(MOBILITY_LINE_CONFIGURATION_URL)},
       {PROXY_URL, Environment.GetEnvironmentVariable(PROXY_URL)},
       {EBONDING_CARE_ORDER_REQUEST_QUEUE_URL, Environment.GetEnvironmentVariable(EBONDING_CARE_ORDER_REQUEST_QUEUE_URL)},
   };
   ```

3. **SQS Message Processing Loop**
   - Iterates through `sqsEvent.Records`
   - Extracts `MobilityLineConfigurationQueueId` from message attributes
   - Validates queue ID > 0 before processing

4. **Exception Handling**
   - Catches all exceptions and logs them as "EXCEPTION" with detailed message
   - Ensures cleanup is always called in finally block

---

## 2. BaseFunctionHandler (Context Initialization)

### Method: `BaseFunctionHandler(ILambdaContext context, bool skipOUSpecificLogic = false)`
**Location**: `AWSFunctionBase.cs:40-45`

#### Low-Level Details:
1. **Context Creation**
   ```csharp
   KeySysLambdaContext keySysLambdaContext = new KeySysLambdaContext(context, skipOUSpecificLogic);
   ```

2. **KeySysLambdaContext Constructor Responsibilities**
   - Initializes database connection strings
   - Sets up logging infrastructure
   - Configures environment-specific settings
   - Loads OU (Organizational Unit) specific configurations unless skipped

---

## 3. GetMobilityConfigurationChangeDetails

### Method: `GetMobilityConfigurationChangeDetails(KeySysLambdaContext context, int id)`
**Location**: `AltaworxMobilityConfigurationChange.cs:206-218`

#### Low-Level Details:
1. **Repository Instantiation**
   ```csharp
   var repo = new MobilityConfigurationChangeRepository(context.CentralDbConnectionString);
   ```

2. **Data Retrieval**
   - Calls `repo.GetUnprocessedChange(id)` 
   - Returns `MobilityConfigurationChange` object or null

3. **Exception Handling**
   - Catches all exceptions and logs with queue ID context
   - Returns null on failure for graceful degradation

---

## 4. MobilityConfigurationChangeRepository.GetUnprocessedChange

### Method: `GetUnprocessedChange(int changeId)`
**Location**: `MobilityConfigurationChangeRepository.cs:40-63`

#### Low-Level Details:
1. **Retry Policy Execution**
   ```csharp
   return sqlRetryPolicy.Execute(() => { ... });
   ```

2. **Database Connection**
   - Uses `SqlConnection(connectionString)`
   - Executes stored procedure `usp_Get_MobilityConfigurationChangeDetails`
   - Parameter: `@queueId = changeId`

3. **Data Reading**
   - Calls `ReadMobilityConfigurationChange(reader)` for data mapping
   - Returns null if no records found

---

## 5. ReadMobilityConfigurationChange

### Method: `ReadMobilityConfigurationChange(IDataRecord reader)`
**Location**: `MobilityConfigurationChangeRepository.cs:99-110`

#### Low-Level Details:
1. **Object Mapping**
   ```csharp
   return new MobilityConfigurationChange
   {
       MobilityDeviceId = (int)reader["MobilityDeviceId"],
       SubscriberNumber = reader["SubscriberNumber"].ToString(),
       Details = reader["MobilityConfigurationChangeDetails"].ToString(),
       ServiceProviderId = (int)reader["ServiceProviderId"],
       IsProcessed = (bool)reader["IsProcessed"],
       TenantId = reader["TenantId"] != DBNull.Value ? (int)reader["TenantId"] : (int?)null
   };
   ```

2. **Data Type Handling**
   - Handles nullable `TenantId` with DBNull check
   - Converts database types to appropriate .NET types

---

## 6. ServiceProviderCommon.GetServiceProvider

### Method: `GetServiceProvider(string connectionString, int serviceProviderId)`
**Location**: `ServiceProviderCommon.cs:45-82`

#### Low-Level Details:
1. **SQL Query Execution**
   ```sql
   SELECT Id, Name, DisplayName, IntegrationId, TenantId, [BillPeriodEndDay], 
          [BillPeriodEndHour], [OptimizationStartHourLocalTime], 
          [ContinuousLastDayOptimizationStartHourLocalTime], [WriteIsEnabled], 
          [RegisterCarrierServiceCallBack] 
   FROM ServiceProvider 
   WHERE id = @serviceProviderId
   ```

2. **Object Construction**
   - Maps all database fields to `ServiceProvider` object
   - Handles nullable integer fields with conditional parsing
   - Uses 120-second timeout for query execution

3. **Integration Type Determination**
   - Returns `IntegrationId` which determines eBonding vs Telegence flow
   - Critical branching point for subsequent processing

---

## 7A. eBonding Branch Flow

### Method: `EnqueueEbondingChangeAsync(KeySysLambdaContext context, IDictionary<string, string> settings, int changeId)`
**Location**: `AltaworxMobilityConfigurationChange.cs:274-304`

#### Low-Level Details:
1. **Queue URL Validation**
   ```csharp
   if (!settings.ContainsKey(EBONDING_CARE_ORDER_REQUEST_QUEUE_URL) || 
       string.IsNullOrWhiteSpace(settings[EBONDING_CARE_ORDER_REQUEST_QUEUE_URL]))
   ```

2. **SQS Client Configuration**
   ```csharp
   using (var client = new AmazonSQSClient(AwsCredentials(context), Amazon.RegionEndpoint.USEast1))
   ```

3. **Message Construction**
   ```csharp
   var request = new SendMessageRequest
   {
       DelaySeconds = 5,
       MessageAttributes = new Dictionary<string, MessageAttributeValue>
       {
           {"ChangeId", new MessageAttributeValue {DataType = "Number", StringValue = changeId.ToString()}}
       },
       MessageBody = "Not used",
       QueueUrl = settings[EBONDING_CARE_ORDER_REQUEST_QUEUE_URL]
   };
   ```

4. **Asynchronous Processing**
   - Sends message to eBonding queue for separate processing
   - Validates HTTP status codes (200-299 range)
   - Early return after enqueuing (no further processing in this lambda)

---

## 7B. Telegence Branch Flow

### Method: `ProcessMobilityConfigurationChange(...)`
**Location**: `AltaworxMobilityConfigurationChange.cs:135-164`

#### Low-Level Details:
1. **Variable Initialization**
   ```csharp
   bool lambdaResponse = false;
   ```

2. **Authentication Retrieval**
   - Calls `TelegenceCommon.GetTelegenceAuthenticationInformation()`
   - Retrieves service provider-specific Telegence credentials

3. **Request Generation**
   - Calls `GenerateTelegenceMobilityConfigurationRequest(details)`
   - Validates request is not null before proceeding

4. **Configuration Update**
   - Calls `TelegenceCommon.UpdateTelegenceMobilityConfiguration()`
   - Passes all authentication, environment, and configuration data

5. **Result Processing**
   - Logs update result
   - Calls `MarkProcessed()` on success
   - Sets `lambdaResponse` for AMOP20 notification

6. **Non-Production Response**
   - Calls `SendResponseToAMOP20()` if not in production environment

---

## 8. GetTelegenceAuthenticationInformation

### Method: `GetTelegenceAuthenticationInformation(string connectionString, int serviceProviderId)`
**Location**: `TelegenceCommon.cs:25-67`

#### Low-Level Details:
1. **Stored Procedure Execution**
   ```csharp
   using (var Cmd = new SqlCommand("usp_Telegence_Get_AuthenticationByProviderId", Conn))
   {
       Cmd.CommandType = CommandType.StoredProcedure;
       Cmd.Parameters.AddWithValue("@providerId", serviceProviderId);
   }
   ```

2. **Authentication Object Construction**
   ```csharp
   telegenceAuthentication = new TelegenceAuthentication()
   {
       TelegenceAuthenticationId = Convert.ToInt32(rdr["integrationAuthenticationId"]),
       ProductionUrl = rdr["productionUrl"].ToString(),
       SandboxUrl = rdr["sandboxUrl"].ToString(),
       ClientId = rdr["ClientId"].ToString(),
       ClientSecret = rdr["ClientSecrect"].ToString(),
       WriteIsEnabled = rdr.GetBoolean(rdr.GetOrdinal("WriteIsEnabled")),
       BillPeriodEndDay = rdr.IsDBNull(billPeriodEndDayIndex) ? 1 : rdr.GetInt32(billPeriodEndDayIndex),
       Password = rdr["password"].ToString(),
       UserName = rdr["username"].ToString(),
   };
   ```

3. **Error Handling**
   - Returns null on exception
   - Uses Debug.WriteLine for exception logging

---

## 9. GenerateTelegenceMobilityConfigurationRequest

### Method: `GenerateTelegenceMobilityConfigurationRequest(MobilityConfigurationChangeDetails details)`
**Location**: `AltaworxMobilityConfigurationChange.cs:166-204`

#### Low-Level Details:
1. **Request Object Initialization**
   ```csharp
   TelegenceMobilityConfigurationRequest request = new TelegenceMobilityConfigurationRequest();
   List<ServiceCharacteristic> serviceCharacteristics = new List<ServiceCharacteristic>();
   ```

2. **Add Operations Processing**
   ```csharp
   if (details?.MobilityConfigurationIDsToAdd != null)
   {
       foreach (var offeringCodeToAdd in details.MobilityConfigurationIDsToAdd)
       {
           ServiceCharacteristic characteristic = new ServiceCharacteristic
           {
               Name = details.ConfigurationType == "MobilityFeature" ? ADD_OFFERING_CODE : SINGLE_USER_CODE,
               Value = offeringCodeToAdd
           };
           serviceCharacteristics.Add(characteristic);
       }
   }
   ```

3. **Remove Operations Processing**
   ```csharp
   if (details?.MobilityConfigurationIDsToRemove != null)
   {
       foreach (var offeringCodeToRemove in details.MobilityConfigurationIDsToRemove)
       {
           ServiceCharacteristic characteristic = new ServiceCharacteristic
           {
               Name = REMOVE_OFFERING_CODE,
               Value = offeringCodeToRemove
           };
           serviceCharacteristics.Add(characteristic);
       }
   }
   ```

4. **Effective Date Processing**
   ```csharp
   if (details?.EffectiveDate != null && DateTime.TryParse(details.EffectiveDate, out var effectiveDate))
   {
       request.effectiveDate = $"{effectiveDate:yyyy-MM-dd}Z";
   }
   ```

5. **Constants Used**
   - `ADD_OFFERING_CODE = "addOfferingCode"`
   - `REMOVE_OFFERING_CODE = "removeOfferingCode"`
   - `SINGLE_USER_CODE = "singleUserCode"`

---

## 10. UpdateTelegenceMobilityConfiguration

### Method: `UpdateTelegenceMobilityConfiguration(...)`
**Location**: `TelegenceCommon.cs:274-356`

#### Low-Level Details:
1. **Write Permission Check**
   ```csharp
   if (telegenceAuthentication.WriteIsEnabled)
   ```

2. **URL Construction**
   ```csharp
   var mobilityConfigurationURL = endpoint + subscriberNo;
   ```

3. **Environment URL Selection**
   ```csharp
   Uri baseUrl = new Uri(telegenceAuthentication.SandboxUrl);
   if (isProduction)
   {
       baseUrl = new Uri(telegenceAuthentication.ProductionUrl);
   }
   ```

4. **Proxy vs Direct Call Logic**
   - **With Proxy**:
     - Creates `ExpandoObject` for headers
     - Builds `PayloadModel` with authentication details
     - Calls `client.PatchWithProxy(proxyUrl, payload, logger)`
   
   - **Without Proxy**:
     - Sets `BaseAddress` directly
     - Adds headers to `DefaultRequestHeaders`
     - Performs direct `client.Patch()` call

5. **JSON Serialization**
   ```csharp
   var jsonContentString = JsonConvert.SerializeObject(request, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
   ```

6. **HTTP Method**: Uses PATCH for mobility configuration updates

---

## 11. MarkProcessed

### Method: `MarkProcessed(KeySysLambdaContext context, MobilityConfigurationChangeDetails details, int id)`
**Location**: `AltaworxMobilityConfigurationChange.cs:220-248`

#### Low-Level Details:
1. **Configuration Type Branching**
   ```csharp
   if (details.ConfigurationType == "RatePlan")
   {
       // Rate plan processing
   }
   else if (details.ConfigurationType == "MobilityFeature")
   {
       // Mobility feature processing
   }
   ```

2. **Rate Plan Processing**
   ```csharp
   var repo = new MobilityConfigurationChangeRepository(context.CentralDbConnectionString);
   repo.CompleteRatePlanChange(id, details.MobilityConfigurationIDsToAdd[0], 
                              details.OptimizationGroup, DateTime.UtcNow, ParameterizedLog(context));
   ```

3. **Mobility Feature Processing**
   - Validates `telegenceDeviceId` is not null or 0
   - Calls `UpdateMobilityDeviceFeature()` with SOC codes and device ID

---

## 12A. CompleteRatePlanChange (Rate Plan Branch)

### Method: `CompleteRatePlanChange(...)`
**Location**: `MobilityConfigurationChangeRepository.cs:65-97`

#### Low-Level Details:
1. **First Stored Procedure Call**
   ```csharp
   Helpers.SqlQueryHelper.ExecuteStoredProcedureWithRowCountResult(logFunction,
       connectionString,
       SQLConstant.StoredProcedureName.UPDATE_MOBILITY_CONFIGURATION_CHANGE_DETAILS,
       parameters,
       SQLConstant.ShortTimeoutSeconds);
   ```

2. **Second Stored Procedure Call**
   ```csharp
   Helpers.SqlQueryHelper.ExecuteStoredProcedureWithRowCountResult(logFunction,
       connectionString,
       SQLConstant.StoredProcedureName.UPDATE_MOBILITY_RATE_PLAN_DEVICE_TABLES,
       parameters,
       SQLConstant.ShortTimeoutSeconds);
   ```

3. **Parameter Construction**
   - Required: `QUEUE_ID`, `RATE_PLAN`
   - Optional: `OPTIMIZATION_GROUP_ID`, `RATE_PLAN_EFFECTIVE_DATE` (only if HasValue)

4. **Retry Policy**: Both calls wrapped in `sqlRetryPolicy.Execute()`

---

## 12B. UpdateMobilityDeviceFeature (Mobility Feature Branch)

### Method: `UpdateMobilityDeviceFeature(KeySysLambdaContext context, string SOCCodes, int deviceId, int changeId)`
**Location**: `AltaworxMobilityConfigurationChange.cs:250-272`

#### Low-Level Details:
1. **Stored Procedure Execution**
   ```csharp
   cmd.CommandText = "dbo.usp_Update_MobilityDeviceFeature";
   cmd.Parameters.AddWithValue("@SOCCodes", SOCCodes);
   cmd.Parameters.AddWithValue("@mobilityDeviceId", deviceId);
   cmd.Parameters.AddWithValue("@processedBy", "UpdateMobilityDeviceFeature_lambda");
   cmd.Parameters.AddWithValue(CommonSQLParameterNames.CHANGE_ID, changeId);
   cmd.CommandTimeout = SQLConstant.ShortTimeoutSeconds;
   ```

2. **Database Operation**
   - Uses `ExecuteNonQuery()` for update operation
   - Updates mobility device feature tables with new SOC codes
   - Tracks change with lambda processor identifier

---

## 13. SendResponseToAMOP20 (Non-Production Only)

### Method: `SendResponseToAMOP20(KeySysLambdaContext context, string jobName, string lambdaResponse)`
**Location**: `AltaworxMobilityConfigurationChange.cs:306-326`

#### Low-Level Details:
1. **HTTP Client Configuration**
   ```csharp
   using (HttpClient client = new HttpClient(new LambdaLoggingHandler()))
   {
       client.BaseAddress = new Uri("https://v1djztyfcg.execute-api.us-east-1.amazonaws.com/dev/migration_job_scheduler");
       client.DefaultRequestHeaders.Add("Accept", "application/json");
   }
   ```

2. **JSON Payload Construction**
   ```csharp
   string jsonRequest = "{\"data\":{ \"data\": { \"path\": \"/carrier_api_save_to_20\",\"job_name\": \"" + jobName + "\",\"response_message\": \"" + lambdaResponse + "\"}}}";
   ```

3. **HTTP POST Operation**
   ```csharp
   var contDevice = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
   HttpResponseMessage response = client.PostAsync(client.BaseAddress, contDevice).Result;
   ```

4. **Response Handling**
   - Logs success or error based on `IsSuccessStatusCode`
   - Reads response body for error logging

---

## 14. CleanUp (Final Cleanup)

### Method: `CleanUp(KeySysLambdaContext context)`
**Location**: `AWSFunctionBase.cs:53-56`

#### Low-Level Details:
1. **Context Cleanup**
   ```csharp
   context.CleanUp();
   ```

2. **Resource Disposal**
   - Closes database connections
   - Disposes logging resources
   - Clears context-specific data
   - Ensures proper memory cleanup

---

## Data Flow Summary

1. **Entry**: SQS message with `MobilityLineConfigurationQueueId`
2. **Retrieval**: Database query to get mobility configuration change details
3. **Service Provider Check**: Determines integration type (eBonding vs Telegence)
4. **Branching**:
   - **eBonding**: Enqueue to separate processing queue
   - **Telegence**: Process immediately with API calls
5. **Processing**: Generate request, authenticate, and update configuration
6. **Completion**: Mark as processed, update device tables, notify AMOP20
7. **Cleanup**: Resource disposal and context cleanup

## Error Handling Patterns

1. **Database Operations**: Retry policies with exponential backoff
2. **HTTP Calls**: Polly retry policies for transient failures
3. **Null Checks**: Comprehensive validation before processing
4. **Exception Logging**: Detailed context information with stack traces
5. **Graceful Degradation**: Continue processing other records on individual failures

## Performance Considerations

1. **Connection Management**: Using statements for automatic disposal
2. **Batch Processing**: SQS records processed in a single lambda invocation
3. **Timeout Settings**: Configurable timeouts for database and HTTP operations
4. **Retry Policies**: Intelligent retry logic to handle transient failures
5. **Async Operations**: Asynchronous patterns for I/O bound operations