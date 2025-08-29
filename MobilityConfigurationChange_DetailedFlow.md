# Mobility Configuration Change - Detailed Low-Level Flow

This document provides a comprehensive, low-level explanation of how the Mobility Configuration Change system processes requests from start to finish, explaining the purpose and mechanics of each step in the flow.

## 1. FunctionHandler (Entry Point)

The journey begins when AWS Lambda receives an SQS event containing mobility configuration change requests. The system is designed to handle multiple records in a single invocation, processing them sequentially to ensure data integrity and proper error handling.

The function handler acts as the orchestrator, extracting essential configuration from environment variables including the Mobility Line Configuration URL for API calls, Proxy URL for secure communication, and eBonding Care Order Request Queue URL for alternative processing paths. Each incoming record contains a MessageId that uniquely identifies the change request and a MobilityLineConfigurationQueueId that references the specific database record to be processed.

## 2. BaseFunctionHandler (Context Initialization)

This stage establishes the operational context for the entire process, creating a KeySysLambdaContext that wraps the AWS Lambda context with additional functionality specific to the business domain. The context initialization phase sets up database connection strings, logging infrastructure, and environment-specific configurations.

The system determines whether it's running in production or sandbox mode, which affects how subsequent API calls are made and which endpoints are used. This context object becomes the central coordination point that carries configuration, logging capabilities, and database connectivity throughout the entire processing pipeline.

## 3. GetMobilityConfigurationChangeDetails

This step bridges the external message queue with the internal database system. Using the queue ID extracted from the SQS message, the system queries the database to retrieve the complete mobility configuration change record. This involves creating a MobilityConfigurationChangeRepository instance and calling its GetUnprocessedChange method.

The repository implements retry logic using Polly policies to handle transient database connectivity issues. The database query executes a stored procedure called 'usp_Get_MobilityConfigurationChangeDetails' which retrieves essential information including the mobility device ID, subscriber number, change details in JSON format, service provider ID, processing status, and tenant information.

## 4. MobilityConfigurationChangeRepository.GetUnprocessedChange

The repository layer provides a robust interface to the database, implementing connection management and retry policies. When GetUnprocessedChange is called, it establishes a SQL connection, creates a parameterized command to prevent SQL injection attacks, and executes the stored procedure with the provided queue ID.

The query results are processed through the ReadMobilityConfigurationChange method, which maps database columns to a MobilityConfigurationChange object. This object contains all the information needed to understand what changes need to be applied, including the device identifier, subscriber number, detailed change specifications, and associated service provider information.

## 5. ReadMobilityConfigurationChange

This method performs the critical task of transforming raw database query results into a strongly-typed business object. It carefully extracts each field from the database reader, handling null values appropriately and ensuring data type conversions are performed safely.

The resulting MobilityConfigurationChange object serves as the data contract for all subsequent processing steps, containing the mobility device ID that identifies which device needs modification, the subscriber number for API calls, detailed change specifications in JSON format, service provider identification for routing decisions, processing status to prevent duplicate processing, and tenant information for multi-tenant scenarios.

## 6. ServiceProviderCommon.GetServiceProvider

With the change details retrieved, the system needs to determine how to process the request based on the service provider's configuration. This step queries the ServiceProvider table using the service provider ID from the change record, retrieving comprehensive provider information including integration type, write permissions, and API configuration details.

The query retrieves critical information such as the provider's name and display name, integration ID that determines the processing path, tenant ID for multi-tenant deployments, billing period configuration, optimization settings, write permissions that control whether changes can actually be applied, and callback registration preferences for carrier services.

This information is essential for the routing decision that follows, as different service providers use different integration mechanisms and have different capabilities enabled.

## 7. Processing Branch Decision

Based on the service provider's IntegrationId, the system makes a critical routing decision that determines the entire subsequent processing flow. This decision point splits the processing into two distinct paths, each optimized for different integration patterns and carrier requirements.

If the integration type is eBonding, the system routes to asynchronous queue-based processing designed for carriers that prefer batch processing or have specific timing requirements. If the integration type is Telegence (or any other type), the system routes to direct API-based processing for real-time configuration changes.

## Branch A: eBonding Path

### 8A. EnqueueEbondingChangeAsync

For eBonding integrations, the system takes an asynchronous approach by placing the change request into a specialized SQS queue for later processing. This design pattern accommodates eBonding systems that may have different processing schedules, batch requirements, or integration constraints.

The method constructs an SQS message with specific attributes including the original change ID for tracking purposes, a delay to allow for any necessary system settling time, and routing information for the downstream eBonding processor. The message is sent to the eBonding Care Order Request Queue, where specialized eBonding handlers will eventually process it according to the eBonding system's requirements and schedule.

This approach provides loose coupling between the mobility configuration system and the eBonding infrastructure, allowing each system to operate at its own pace while maintaining reliable message delivery and processing guarantees.

## Branch B: Telegence Path

### 8B. ProcessMobilityConfigurationChange

For Telegence and other direct API integrations, the system begins immediate processing of the configuration change. This method orchestrates the complex sequence of API authentication, request preparation, API execution, and result processing required for real-time mobility configuration updates.

The process starts by establishing the operational context and logging the beginning of processing for audit and debugging purposes. This synchronous approach is designed for carriers that support real-time API interactions and can provide immediate feedback on the success or failure of configuration changes.

### 9B. GetTelegenceAuthenticationInformation

Before any API calls can be made to Telegence systems, the system must retrieve and prepare the necessary authentication credentials. This step queries the integration authentication table using the service provider ID to retrieve provider-specific API credentials and configuration.

The authentication retrieval process gets the Telegence authentication ID for tracking API usage, production and sandbox URLs for environment-specific routing, client ID and client secret for API authentication, write permissions to ensure the provider allows configuration changes, billing period configuration for timing considerations, and username/password for additional authentication layers if required.

This information is encapsulated in a TelegenceAuthentication object that carries all the necessary credentials and configuration for subsequent API interactions with the Telegence platform.

### 10B. GenerateTelegenceMobilityConfigurationRequest

With authentication established, the system transforms the generic mobility configuration change into a Telegence-specific API request format. This transformation process involves parsing the change details JSON to extract specific configuration items, mapping mobility features to Telegence service characteristics, and formatting the request according to Telegence API specifications.

The method constructs ServiceCharacteristic objects for each configuration change, using different characteristic names depending on the type of change (adding features uses 'addOfferingCode', removing features uses 'removeOfferingCode', and rate plan changes use 'singleUserCode'). The effective date is formatted according to Telegence's expected ISO date format, and all characteristics are assembled into a complete TelegenceMobilityConfigurationRequest.

### 11B. UpdateTelegenceMobilityConfiguration

This step performs the actual API call to the Telegence platform to apply the configuration changes. The method handles both direct API calls and proxy-mediated calls depending on the network configuration and security requirements of the deployment environment.

For direct calls, the system constructs an HTTP client with the appropriate base URL (production or sandbox), adds the required authentication headers (app-id and app-secret), serializes the request object to JSON format, and sends a PATCH request to the Telegence mobility configuration endpoint. For proxy calls, the system constructs a payload object containing authentication, endpoint, and content information, sends the request through the configured proxy service, and processes the response through the proxy infrastructure.

The method returns a boolean indicating success or failure, which determines whether subsequent processing steps should be executed.

### 12B. MarkProcessed

Upon successful API execution, the system updates the database to reflect that the change has been processed, preventing duplicate processing and maintaining accurate audit trails. This step involves different processing paths depending on the type of configuration change being applied.

For rate plan changes, the system calls CompleteRatePlanChange which updates both the mobility configuration change table and related rate plan device tables with the new rate plan code, optimization group information, and effective date. For mobility feature changes, the system calls UpdateMobilityDeviceFeature which updates the device tables with the current SOC (Service Order Code) configuration, ensuring that the local database accurately reflects the changes applied to the carrier's system.

This dual update pattern ensures data consistency between the local optimization database and the carrier's authoritative configuration systems.

### 13B. CompleteRatePlanChange / UpdateMobilityDeviceFeature

These specialized methods handle the database updates required for different types of configuration changes. CompleteRatePlanChange executes stored procedures to update rate plan information across multiple related tables, ensuring that billing, optimization, and reporting systems all have consistent rate plan data.

UpdateMobilityDeviceFeature focuses on updating SOC code configurations in the device tables, which affects how features and services are managed and optimized for specific devices. Both methods implement proper transaction handling and error logging to ensure data integrity and provide troubleshooting information when issues occur.

### 14B. SendResponseToAMOP20 (Non-Production Only)

In non-production environments, the system sends status updates to the AMOP 2.0 migration job scheduler, providing feedback on the success or failure of configuration changes. This integration supports testing and validation workflows by ensuring that test systems receive appropriate notifications about configuration change outcomes.

The method constructs a JSON payload containing job identification, response status, and any relevant error information, then sends this information to the AMOP 2.0 API endpoint using HTTP POST. This feedback mechanism is disabled in production environments to avoid unnecessary system coupling and potential performance impacts.

## 15. CleanUp (Final Cleanup)

Regardless of which processing path was taken, the system concludes with a cleanup phase that ensures proper resource disposal and logging completion. This step calls the CleanUp method on the KeySysLambdaContext, which handles closing database connections, flushing log buffers, and releasing any other resources that were acquired during processing.

The cleanup phase also ensures that any temporary objects or connections are properly disposed of, preventing memory leaks and ensuring that the Lambda function is ready for subsequent invocations. This disciplined resource management is essential for maintaining optimal performance in the serverless AWS Lambda environment where function instances may be reused across multiple invocations.

## Error Handling and Resilience

Throughout the entire flow, the system implements comprehensive error handling and resilience patterns. Database operations are protected by retry policies that handle transient connectivity issues, API calls include timeout and error response handling, and all major operations include extensive logging for troubleshooting and audit purposes.

The system is designed to fail gracefully, ensuring that partial failures don't leave the system in an inconsistent state and that all errors are properly logged and reported for operational monitoring and debugging.

## Summary

This mobility configuration change system represents a sophisticated integration platform that bridges mobile device management systems with carrier APIs and eBonding infrastructures. The flow accommodates different carrier integration patterns, provides robust error handling and retry mechanisms, maintains data consistency across multiple systems, supports both real-time and asynchronous processing patterns, and includes comprehensive logging and monitoring capabilities for operational excellence.