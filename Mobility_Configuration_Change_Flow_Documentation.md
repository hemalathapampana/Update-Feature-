# Mobility Configuration Change - Detailed Method Flow Documentation

## Overview
This document provides a comprehensive low-level breakdown of the Mobility Configuration Change processing flow, detailing each step and the underlying operations performed at each stage.

---

## 1. FunctionHandler (Entry Point)

### Process Description
The FunctionHandler serves as the AWS Lambda entry point for processing SQS events containing mobility configuration change requests.

### Low-Level Operations
- **Event Reception**: Receives AWS SQS events through the Lambda runtime
- **Event Validation**: Validates the incoming SQS event structure and ensures records exist
- **Message Iteration**: Iterates through each SQS record in the event batch (supports multiple records per invocation)
- **Message Attribute Extraction**: Extracts the `MobilityLineConfigurationQueueId` from SQS message attributes
- **Integer Conversion**: Converts the queue ID string value to integer for database operations
- **Configuration Loading**: Loads environment variables for:
  - Mobility Line Configuration URL (API endpoint)
  - Proxy URL (for network routing)
  - eBonding Care Order Request Queue URL (for eBonding workflows)
- **Environment Fallback**: Falls back to ClientContext environment variables if standard environment variables are not available
- **Logging Initialization**: Sets up comprehensive logging for the entire process flow
- **Error Handling Setup**: Establishes try-catch blocks for exception management throughout the process

---

## 2. BaseFunctionHandler (Context Initialization)

### Process Description
BaseFunctionHandler initializes the execution context and sets up the foundational components required for all subsequent operations.

### Low-Level Operations
- **Lambda Context Wrapping**: Wraps the AWS Lambda context into a KeySysLambdaContext object
- **Database Connection String Resolution**: Resolves central database connection strings from environment/configuration
- **OU-Specific Logic Setup**: Initializes organizational unit specific logic and settings
- **Logger Initialization**: Sets up structured logging with context-aware information
- **Production Environment Detection**: Determines if the function is running in production or sandbox environment
- **Security Context Setup**: Establishes security context for database and API operations
- **Base64 Service Initialization**: Initializes encoding/decoding services for sensitive data
- **Provider Settings Loading**: Loads general provider settings including AWS credentials and timeouts
- **Connection Pool Management**: Establishes database connection pooling parameters
- **Retry Policy Configuration**: Sets up retry policies for database and external API calls

---

## 3. GetMobilityConfigurationChangeDetails

### Process Description
This method retrieves the complete details of a mobility configuration change request from the database using the queue ID.

### Low-Level Operations
- **Repository Instantiation**: Creates a new MobilityConfigurationChangeRepository instance with connection string
- **Parameter Validation**: Validates the incoming queue ID parameter
- **Database Connection Establishment**: Opens a SQL connection to the central database
- **Stored Procedure Execution**: Executes `usp_Get_MobilityConfigurationChangeDetails` stored procedure
- **Parameter Binding**: Binds the queue ID parameter to the stored procedure call
- **Result Set Processing**: Processes the returned result set from the database
- **Object Mapping**: Maps database columns to MobilityConfigurationChange object properties
- **Null Checking**: Performs null checking on database field values before assignment
- **Data Type Conversion**: Converts database data types to appropriate .NET types
- **Connection Resource Management**: Ensures proper disposal of database connections and commands
- **Exception Logging**: Logs any database-related exceptions with detailed context information

---

## 4. MobilityConfigurationChangeRepository.GetUnprocessedChange

### Process Description
This repository method implements the actual database interaction to retrieve unprocessed mobility configuration changes with built-in retry logic.

### Low-Level Operations
- **Retry Policy Execution**: Wraps the database call in a Polly retry policy (configured for 3 retries)
- **SQL Connection Management**: Creates and manages SQL connection lifecycle
- **Stored Procedure Configuration**: Configures the stored procedure call with proper command type
- **Parameter Assignment**: Assigns the queue ID parameter using parameterized queries to prevent SQL injection
- **Command Timeout Setting**: Sets appropriate command timeout values for database operations
- **Connection Opening**: Opens the database connection within the retry context
- **Data Reader Execution**: Executes the command and creates a data reader for result processing
- **Row Existence Check**: Checks if any rows are returned from the database query
- **Data Transformation**: Calls ReadMobilityConfigurationChange to transform raw data
- **Resource Cleanup**: Ensures proper disposal of connections, commands, and readers
- **Exception Propagation**: Handles and propagates database exceptions through the retry mechanism

---

## 5. ReadMobilityConfigurationChange

### Process Description
This private method transforms raw database record data into a strongly-typed MobilityConfigurationChange object.

### Low-Level Operations
- **Field Extraction**: Extracts individual field values from the IDataRecord
- **Type Casting**: Performs safe type casting for each database field
- **Integer Conversion**: Converts database integer fields (MobilityDeviceId, ServiceProviderId)
- **String Processing**: Processes string fields (SubscriberNumber, Details) with null handling
- **Boolean Conversion**: Safely converts IsProcessed boolean field from database
- **Nullable Integer Handling**: Handles nullable TenantId field with DBNull checks
- **Object Instantiation**: Creates new MobilityConfigurationChange instance
- **Property Assignment**: Assigns all extracted values to the corresponding object properties
- **Data Validation**: Performs implicit validation through type conversion
- **Memory Management**: Ensures efficient memory usage during object creation

---

## 6. ServiceProviderCommon.GetServiceProvider

### Process Description
This static method retrieves comprehensive service provider information including integration settings and operational flags.

### Low-Level Operations
- **Connection String Validation**: Validates the provided database connection string
- **SQL Query Construction**: Constructs parameterized SQL query for service provider retrieval
- **Multi-Field Selection**: Selects multiple fields including billing, optimization, and integration settings
- **Parameter Binding**: Binds service provider ID parameter to prevent SQL injection
- **Timeout Configuration**: Sets extended timeout (120 seconds) for potentially complex queries
- **Connection Lifecycle Management**: Opens and properly manages database connection
- **Data Reader Processing**: Processes the returned data reader row by row
- **Field Ordinal Resolution**: Resolves field ordinals for efficient data access
- **Null Value Handling**: Handles DBNull values for optional fields (TenantId, BillPeriodEndDay, etc.)
- **Type Conversion**: Converts database values to appropriate .NET types
- **Boolean Field Processing**: Processes WriteIsEnabled and RegisterCarrierServiceCallBack boolean fields
- **Object Construction**: Constructs complete ServiceProvider object with all properties
- **Resource Disposal**: Ensures proper cleanup of database resources

---

## Integration Type Branching

### Decision Logic
After retrieving the service provider information, the system determines the integration type and branches accordingly:

#### Integration Type Resolution
- **IntegrationId Extraction**: Extracts IntegrationId from ServiceProvider object
- **Enum Conversion**: Converts integer IntegrationId to IntegrationType enum
- **Default Handling**: Defaults to Telegence integration if IntegrationId is null
- **Branch Decision**: Uses integration type to determine processing path

---

## Branch A: eBonding Path

### 7A. EnqueueEbondingChangeAsync

#### Process Description
This method handles mobility configuration changes for eBonding service providers by queuing them for asynchronous processing.

#### Low-Level Operations
- **Write Permission Check**: Verifies that WriteIsEnabled is true for the service provider
- **Queue URL Validation**: Validates that eBonding queue URL is configured and not empty
- **AWS Credentials Setup**: Retrieves and configures AWS credentials from context
- **SQS Client Initialization**: Creates Amazon SQS client with US-East-1 region configuration
- **Message Request Construction**: Builds SendMessageRequest with:
  - DelaySeconds set to 5 for processing delay
  - Message attributes containing ChangeId as a Number type
  - Static message body (actual data in attributes)
  - Target queue URL from configuration
- **Message Dispatch**: Sends the message to the eBonding care order request queue
- **Response Validation**: Checks HTTP status code for successful message delivery (200-299 range)
- **Error Logging**: Logs detailed error information if message dispatch fails
- **Resource Cleanup**: Properly disposes of SQS client resources
- **Exception Handling**: Captures and logs any AWS-related exceptions

---

## Branch B: Telegence Path

### 7B. ProcessMobilityConfigurationChange

#### Process Description
This method orchestrates the complete Telegence mobility configuration change process, from authentication to final processing.

#### Low-Level Operations
- **Lambda Response Initialization**: Initializes boolean flag to track overall operation success
- **Context Logging**: Logs entry into the ProcessMobilityConfigurationChange method
- **Authentication Retrieval**: Calls TelegenceCommon.GetTelegenceAuthenticationInformation
- **Request Generation**: Generates the Telegence API request payload
- **Request Validation**: Validates that the generated request is not null
- **API Call Execution**: Calls TelegenceCommon.UpdateTelegenceMobilityConfiguration
- **Result Evaluation**: Evaluates the boolean result from the API call
- **Success Path Processing**: If successful, calls MarkProcessed method
- **Response Tracking**: Updates lambdaResponse flag based on operation outcome
- **Non-Production Handling**: For non-production environments, sends response to AMOP20
- **Status Determination**: Determines final status ("Success" or "Failed") for AMOP20
- **Comprehensive Logging**: Logs all major steps and outcomes for debugging

### 8B. GetTelegenceAuthenticationInformation

#### Process Description
This method retrieves comprehensive Telegence authentication credentials and configuration from the database.

#### Low-Level Operations
- **Authentication Object Initialization**: Initializes TelegenceAuthentication object to null
- **Database Connection Setup**: Creates SQL connection using provided connection string
- **Stored Procedure Configuration**: Sets up call to `usp_Telegence_Get_AuthenticationByProviderId`
- **Provider ID Parameter Binding**: Binds service provider ID parameter
- **Connection Opening**: Opens database connection for query execution
- **Data Reader Creation**: Creates SQL data reader for result processing
- **Field Extraction**: Extracts multiple authentication fields:
  - integrationAuthenticationId (converted to int)
  - productionUrl and sandboxUrl (string values)
  - ClientId and ClientSecret (authentication credentials)
  - WriteIsEnabled (boolean flag)
  - BillPeriodEndDay (nullable int with default value 1)
  - password and username (additional credentials)
- **Null Handling**: Handles potential null values, especially for BillPeriodEndDay
- **Object Population**: Populates TelegenceAuthentication object with all extracted values
- **Single Record Processing**: Processes only the first record and breaks loop
- **Resource Management**: Ensures proper closure of connections and readers
- **Exception Handling**: Catches and logs any database-related exceptions

### 9B. GenerateTelegenceMobilityConfigurationRequest

#### Process Description
This method constructs the Telegence API request payload based on the mobility configuration change details.

#### Low-Level Operations
- **Request Object Initialization**: Creates new TelegenceMobilityConfigurationRequest instance
- **Service Characteristics List Creation**: Initializes List<ServiceCharacteristic> for API payload
- **Addition Operations Processing**: Processes MobilityConfigurationIDsToAdd collection:
  - Iterates through each offering code to add
  - Determines characteristic name based on ConfigurationType:
    - "MobilityFeature" → uses ADD_OFFERING_CODE constant
    - Other types → uses SINGLE_USER_CODE constant
  - Creates ServiceCharacteristic object with name-value pairs
  - Adds each characteristic to the collection
- **Removal Operations Processing**: Processes MobilityConfigurationIDsToRemove collection:
  - Iterates through each offering code to remove
  - Uses REMOVE_OFFERING_CODE constant for all removal operations
  - Creates ServiceCharacteristic objects for removals
  - Adds removal characteristics to the collection
- **Effective Date Processing**: Handles EffectiveDate field:
  - Attempts to parse the EffectiveDate string
  - Formats successful parse as "yyyy-MM-dd"Z
  - Assigns formatted date to request.effectiveDate
- **Request Assembly**: Assigns the complete serviceCharacteristic collection to the request
- **Return Processing**: Returns the fully constructed request object

### 10B. UpdateTelegenceMobilityConfiguration

#### Process Description
This method executes the actual HTTP API call to Telegence to update mobility configuration, supporting both direct and proxy-based communication.

#### Low-Level Operations
- **Write Permission Validation**: Checks telegenceAuthentication.WriteIsEnabled flag
- **URL Construction**: Builds complete mobility configuration URL by appending subscriber number
- **HTTP Client Initialization**: Creates new HttpClient instance for API communication
- **Environment-Based URL Selection**: Selects appropriate base URL:
  - Production environment → uses telegenceAuthentication.ProductionUrl
  - Non-production → uses telegenceAuthentication.SandboxUrl
- **Proxy vs Direct Decision**: Determines communication method based on proxyUrl parameter

#### Proxy Communication Path:
- **Header Content Construction**: Creates ExpandoObject with app-id and app-secret
- **Header Serialization**: Serializes header content to JSON string
- **Request Serialization**: Serializes request object with NullValueHandling.Ignore
- **Payload Model Creation**: Constructs PayloadModel with:
  - AuthenticationType set to TELEGENCEAUTH
  - Complete mobility configuration URL
  - Serialized header and JSON content
  - Base URL and authentication type
- **Proxy API Call**: Executes PatchWithProxy method through HTTP client extension
- **Proxy Response Processing**: Processes proxy response for success/failure
- **Proxy Error Logging**: Logs detailed proxy-specific error information

#### Direct Communication Path:
- **Base Address Configuration**: Sets HttpClient.BaseAddress to complete URL
- **Header Assignment**: Adds app-id and app-secret to default request headers
- **Content Serialization**: Serializes request object to JSON string
- **String Content Creation**: Creates StringContent with UTF-8 encoding and application/json media type
- **PATCH Request Execution**: Executes HTTP PATCH request using client.Patch method
- **Response Status Evaluation**: Checks response.IsSuccessStatusCode for operation success
- **Direct Error Handling**: Handles exceptions specific to direct HTTP communication
- **Direct Error Logging**: Logs detailed error information for failed direct calls

#### Common Operations:
- **Success Return**: Returns true for successful operations
- **Failure Return**: Returns false for failed operations
- **Write Disabled Handling**: Returns false and logs warning when writes are disabled
- **Resource Disposal**: Properly disposes of HttpClient resources
- **Comprehensive Logging**: Logs all significant steps and outcomes

### 11B. MarkProcessed

#### Process Description
This method marks the mobility configuration change as processed and performs configuration-type-specific completion operations.

#### Low-Level Operations
- **Method Entry Logging**: Logs entry into MarkProcessed with queue ID
- **Configuration Type Evaluation**: Checks details.ConfigurationType for processing logic
- **Exception Handling Setup**: Wraps operations in try-catch for error management

#### Rate Plan Processing Path:
- **Repository Re-instantiation**: Creates new MobilityConfigurationChangeRepository instance
- **Rate Plan Completion**: Calls CompleteRatePlanChange with:
  - Queue ID for identification
  - First mobility configuration ID from MobilityConfigurationIDsToAdd
  - Optimization group from details.OptimizationGroup
  - Current UTC timestamp
  - Parameterized logging function
- **Database Update Operations**: Executes two stored procedures:
  - Updates mobility configuration change details
  - Updates mobility rate plan device tables

#### Mobility Feature Processing Path:
- **SOC Codes Extraction**: Extracts SOC codes from details.MobilityConfigurationsCurrent
- **Telegence Device ID Validation**: Validates details.telegenceDeviceId:
  - Checks for null or zero values
  - Logs exception and returns early for invalid IDs
- **Device Feature Update**: Calls UpdateMobilityDeviceFeature with:
  - SOC codes string
  - Telegence device ID (cast to int)
  - Queue ID for tracking
- **Database Operation**: Executes stored procedure to update device features

#### Error Handling:
- **Exception Capture**: Catches all exceptions during processing
- **Detailed Error Logging**: Logs exceptions with queue ID and full error messages
- **Graceful Degradation**: Continues processing even if marking fails

### 12B. CompleteRatePlanChange (Repository Method)

#### Process Description
This method completes rate plan changes by updating multiple database tables with new rate plan information.

#### Low-Level Operations
- **First Update Operation**: Updates mobility configuration change details
  - Creates parameter list with QUEUE_ID
  - Executes UPDATE_MOBILITY_CONFIGURATION_CHANGE_DETAILS stored procedure
  - Uses retry policy for resilience
  - Applies short timeout configuration
- **Second Update Preparation**: Prepares parameters for device table updates:
  - Adds QUEUE_ID parameter
  - Adds RATE_PLAN parameter with new rate plan code
  - Conditionally adds OPTIMIZATION_GROUP_ID if provided
  - Conditionally adds RATE_PLAN_EFFECTIVE_DATE if provided
- **Device Table Update**: Updates mobility rate plan device tables
  - Executes UPDATE_MOBILITY_RATE_PLAN_DEVICE_TABLES stored procedure
  - Uses same retry policy and timeout configuration
  - Processes all conditional parameters
- **Retry Policy Application**: Both operations use sqlRetryPolicy for fault tolerance
- **Parameter Management**: Dynamically builds parameter lists based on available data
- **Error Propagation**: Allows retry policy to handle and propagate exceptions

### 13B. UpdateMobilityDeviceFeature

#### Process Description
This method updates mobility device features in the database using SOC (Service Option Code) information.

#### Low-Level Operations
- **Parameter Logging**: Logs SOC codes and device ID for debugging
- **Database Connection Creation**: Creates new SQL connection using context connection string
- **Command Configuration**: Sets up stored procedure command:
  - CommandType set to StoredProcedure
  - CommandText set to "dbo.usp_Update_MobilityDeviceFeature"
  - Short timeout configuration applied
- **Parameter Assignment**: Adds required parameters:
  - @SOCCodes with the SOC codes string
  - @mobilityDeviceId with the device identifier
  - @processedBy with "UpdateMobilityDeviceFeature_lambda" identifier
  - @changeId with the change tracking ID
- **Connection Management**: Opens database connection within using block
- **Command Execution**: Executes the stored procedure using ExecuteNonQuery
- **Resource Cleanup**: Automatically closes connection through using block disposal
- **No Return Processing**: Method performs update operation without return value

### 14B. SendResponseToAMOP20 (Non-Production)

#### Process Description
This method sends processing results back to AMOP 2.0 system for non-production environments only.

#### Low-Level Operations
- **HTTP Client Initialization**: Creates HttpClient with LambdaLoggingHandler for request logging
- **Base Address Configuration**: Sets base address to AMOP 2.0 migration job scheduler endpoint
- **Header Configuration**: Adds Accept header with "application/json" media type
- **JSON Request Construction**: Builds JSON request string with:
  - Nested data structure for AMOP 2.0 format
  - Path set to "/carrier_api_save_to_20"
  - Job name parameter (e.g., "change_rate_plan_smi")
  - Response message indicating success or failure status
- **String Content Creation**: Creates StringContent with:
  - JSON request as content
  - UTF-8 encoding
  - "application/json" media type
- **HTTP POST Execution**: Executes POST request to the configured endpoint
- **Response Evaluation**: Checks response.IsSuccessStatusCode for operation success
- **Success Processing**: For successful responses:
  - Reads response body asynchronously
  - Logs successful transmission to AMOP 2.0
- **Error Processing**: For failed responses:
  - Reads error response body
  - Logs detailed error information
- **Resource Management**: Automatically disposes HttpClient through using block

---

## 8. CleanUp (Final Cleanup)

### Process Description
CleanUp performs final resource cleanup and ensures all connections and resources are properly disposed of before function completion.

### Low-Level Operations
- **Context Cleanup Delegation**: Calls context.CleanUp() method for comprehensive cleanup
- **Database Connection Disposal**: Ensures all database connections are properly closed and disposed
- **HTTP Client Disposal**: Disposes of any remaining HTTP client instances
- **Memory Management**: Triggers garbage collection hints for large object cleanup
- **Logger Cleanup**: Flushes any pending log entries and closes logging resources
- **Connection Pool Cleanup**: Returns database connections to the connection pool
- **Temporary Resource Disposal**: Cleans up any temporary files or cached resources
- **Exception Context Clearing**: Clears any exception context information
- **Thread Resource Cleanup**: Ensures proper cleanup of thread-local resources
- **Performance Counter Reset**: Resets any performance monitoring counters
- **Security Context Cleanup**: Clears any security context information
- **Final State Validation**: Validates that all resources have been properly disposed

---

## Error Handling and Resilience Patterns

### Database Operations
- **Retry Policies**: All database operations use Polly retry policies with 3 maximum retries
- **Connection Resilience**: Automatic connection recovery and retry for transient failures
- **Timeout Management**: Configurable timeouts for different operation types
- **Parameter Validation**: Comprehensive parameter validation before database calls

### API Communications
- **HTTP Retry Logic**: Built-in retry mechanisms for HTTP API calls
- **Proxy Fallback**: Automatic switching between direct and proxy communication
- **Authentication Validation**: Comprehensive authentication credential validation
- **Response Validation**: Detailed response status and content validation

### Resource Management
- **Using Block Patterns**: Consistent use of using blocks for resource disposal
- **Exception Propagation**: Proper exception handling and propagation through call stack
- **Logging Integration**: Comprehensive logging at all error points
- **Graceful Degradation**: System continues processing even when non-critical operations fail

---

## Configuration and Environment Management

### Environment Variables
- **MobilityLineConfigurationURL**: API endpoint for Telegence mobility configuration updates
- **ProxyUrl**: Proxy server URL for network routing in restricted environments
- **eBondingCareOrderRequestQueueUrl**: SQS queue URL for eBonding workflow processing

### Database Configuration
- **Connection Strings**: Multiple connection strings for different database contexts
- **Timeout Values**: Configurable timeout values for different operation types
- **Retry Policies**: Configurable retry counts and backoff strategies

### Integration Settings
- **Service Provider Settings**: Per-provider configuration for integration behavior
- **Authentication Credentials**: Secure storage and retrieval of API credentials
- **Feature Flags**: WriteIsEnabled and other operational flags for controlled deployment

---

## Performance and Scalability Considerations

### Batch Processing
- **SQS Batch Support**: Handles multiple messages per Lambda invocation
- **Efficient Resource Usage**: Reuses connections and clients across batch items
- **Memory Management**: Careful memory management for large batch processing

### Database Optimization
- **Parameterized Queries**: All database calls use parameterized queries for security and performance
- **Connection Pooling**: Efficient database connection pooling and reuse
- **Bulk Operations**: Bulk update capabilities for rate plan and feature changes

### API Efficiency
- **HTTP Connection Reuse**: Efficient HTTP connection management
- **Payload Optimization**: Minimal payload construction for API calls
- **Response Caching**: Strategic caching of authentication and configuration data