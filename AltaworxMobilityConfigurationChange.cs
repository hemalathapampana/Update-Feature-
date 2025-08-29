using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Altaworx.AWS.Core;
using Altaworx.AWS.Core.Models;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amop.Core.Constants;
using Amop.Core.Logger;
using Amop.Core.Models;
using Amop.Core.Models.Telegence.Api;
using Amop.Core.Repositories;
using Amop.Core.Services.Base64Service;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AltaworxMobilityConfigurationChange
{
    public class Function : AwsFunctionBase
    {
        private const string MOBILITY_LINE_CONFIGURATION_URL = "MobilityLineConfigurationURL";
        private const string PROXY_URL = "ProxyUrl";
        private const string EBONDING_CARE_ORDER_REQUEST_QUEUE_URL = "eBondingCareOrderRequestQueueUrl";

        private const string ADD_OFFERING_CODE = "addOfferingCode";
        private const string REMOVE_OFFERING_CODE = "removeOfferingCode";
        private const string SINGLE_USER_CODE = "singleUserCode";

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="sqsEvent"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            KeySysLambdaContext keysysContext = null;
            try
            {
                keysysContext = BaseFunctionHandler(context);

                var settings = new Dictionary<string, string>
                {
                    {MOBILITY_LINE_CONFIGURATION_URL, Environment.GetEnvironmentVariable(MOBILITY_LINE_CONFIGURATION_URL)},
                    {PROXY_URL, Environment.GetEnvironmentVariable(PROXY_URL)},
                    {EBONDING_CARE_ORDER_REQUEST_QUEUE_URL, Environment.GetEnvironmentVariable(EBONDING_CARE_ORDER_REQUEST_QUEUE_URL)},
                };

                if (string.IsNullOrEmpty(settings[MOBILITY_LINE_CONFIGURATION_URL]))
                {
                    settings = new Dictionary<string, string>
                    {
                        {MOBILITY_LINE_CONFIGURATION_URL, context.ClientContext.Environment[MOBILITY_LINE_CONFIGURATION_URL]},
                        {PROXY_URL, context.ClientContext.Environment[PROXY_URL]},
                        {EBONDING_CARE_ORDER_REQUEST_QUEUE_URL, context.ClientContext.Environment[EBONDING_CARE_ORDER_REQUEST_QUEUE_URL]},
                    };
                }

                if (sqsEvent?.Records.Count > 0)
                {
                    LogInfo(keysysContext, "STATUS", $"Beginning to process {sqsEvent.Records.Count} records...");

                    foreach (var record in sqsEvent.Records)
                    {
                        LogInfo(keysysContext, "MessageId", record.MessageId);
                        LogInfo(keysysContext, "EventSource", record.EventSource);
                        LogInfo(keysysContext, "Body", record.Body);

                        var id = 0;
                        if (record.MessageAttributes.ContainsKey("MobilityLineConfigurationQueueId"))
                        {
                            id = Convert.ToInt32(record.MessageAttributes["MobilityLineConfigurationQueueId"].StringValue);
                        }

                        if (id > 0)
                        {
                            var mobilityConfigurationChange = GetMobilityConfigurationChangeDetails(keysysContext, id);
                            if (mobilityConfigurationChange == null)
                            {
                                LogInfo(keysysContext, "EXCEPTION", $"Could not find mobility configuration change with id {id}");
                                return;
                            }

                            var serviceProvider = ServiceProviderCommon.GetServiceProvider(keysysContext.CentralDbConnectionString, mobilityConfigurationChange.ServiceProviderId);
                            var integrationId = serviceProvider?.IntegrationId;
                            var integrationType = integrationId.HasValue ? (IntegrationType)integrationId : IntegrationType.Telegence;
                            if (integrationType == IntegrationType.eBonding)
                            {
                                if (serviceProvider != null && serviceProvider.WriteIsEnabled)
                                {
                                    await EnqueueEbondingChangeAsync(keysysContext, settings, id);
                                }
                                else
                                {
                                    LogInfo(keysysContext, "WARN", "Write disabled for eBonding Service Provider");
                                }
                                return;
                            }

                            var details = JsonConvert.DeserializeObject<MobilityConfigurationChangeDetails>(mobilityConfigurationChange.Details);
                            if (!string.IsNullOrEmpty(mobilityConfigurationChange.SubscriberNumber) &&
                                (details.MobilityConfigurationIDsToAdd?.Count > 0 ||
                                 details.MobilityConfigurationIDsToRemove?.Count > 0))
                            {
                                ProcessMobilityConfigurationChange(keysysContext, mobilityConfigurationChange, details, settings, id);
                            }
                            else
                            {
                                LogInfo(keysysContext, "INFO", $"No valid records to process. QueueId {id} has already been processed.");
                            }
                        }
                        else
                        {
                            LogInfo(keysysContext, "INFO", "No valid records to process");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogInfo(keysysContext, "EXCEPTION", $"Mobility Line Configuration Exception: {e.Message}");
            }

            CleanUp(keysysContext);
        }

        private void ProcessMobilityConfigurationChange(KeySysLambdaContext context,
            MobilityConfigurationChange mobilityConfigurationChange,
            MobilityConfigurationChangeDetails details, IDictionary<string, string> settings, int id)
        {
            bool lambdaResponse = false;
            LogInfo(context, "INFO", $"ProcessMobilityConfigurationChange()");

            var telegenceAuthentication = TelegenceCommon.GetTelegenceAuthenticationInformation(context.CentralDbConnectionString, mobilityConfigurationChange.ServiceProviderId);


            var request = GenerateTelegenceMobilityConfigurationRequest(details);

            if (request != null)
            {
                var result = TelegenceCommon.UpdateTelegenceMobilityConfiguration(context.logger, new Base64Service(),
                    telegenceAuthentication, context.IsProduction, request, mobilityConfigurationChange.SubscriberNumber,
                    settings[MOBILITY_LINE_CONFIGURATION_URL], settings[PROXY_URL]);

                LogInfo(context, "INFO", $"The update feature result: {result.ToString()})");
                if (result)
                {
                    MarkProcessed(context, details, id);
                }
                lambdaResponse = result;
            }
            if (!context.IsProduction)
            {
                SendResponseToAMOP20(context, "change_rate_plan_smi", lambdaResponse == true ? "Success" : "Failed");
            }
        }

        private TelegenceMobilityConfigurationRequest GenerateTelegenceMobilityConfigurationRequest(MobilityConfigurationChangeDetails details)
        {
            TelegenceMobilityConfigurationRequest request = new TelegenceMobilityConfigurationRequest();
            List<ServiceCharacteristic> serviceCharacteristics = new List<ServiceCharacteristic>();

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

            if (details?.EffectiveDate != null && DateTime.TryParse(details.EffectiveDate, out var effectiveDate))
            {
                request.effectiveDate = $"{effectiveDate:yyyy-MM-dd}Z";
            }

            request.serviceCharacteristic = serviceCharacteristics;
            return request;
        }

        private MobilityConfigurationChange GetMobilityConfigurationChangeDetails(KeySysLambdaContext context, int id)
        {
            try
            {
                var repo = new MobilityConfigurationChangeRepository(context.CentralDbConnectionString);
                return repo.GetUnprocessedChange(id);
            }
            catch (Exception e)
            {
                LogInfo(context, "EXCEPTION", $"Failed to get Mobility Configuration Changes from the DB for Queue Id {id}, Message: {e.Message}");
                return null;
            }
        }

        private void MarkProcessed(KeySysLambdaContext context, MobilityConfigurationChangeDetails details, int id)
        {
            LogInfo(context, "SUB", $"MarkProcessed({id})");
            try
            {
                if (details.ConfigurationType == "RatePlan")
                {
                    var repo = new MobilityConfigurationChangeRepository(context.CentralDbConnectionString);
                    repo.CompleteRatePlanChange(id, details.MobilityConfigurationIDsToAdd[0], details.OptimizationGroup, DateTime.UtcNow, ParameterizedLog(context));
                }
                else if (details.ConfigurationType == "MobilityFeature")
                {
                    LogInfo(context, "SUB", $"Update SOCCodes to database.");
                    //TODO update device and Telegence device tables with manual feature changes.
                    var SOCCodes = details.MobilityConfigurationsCurrent;
                    var telegenceDeviceId = details.telegenceDeviceId;
                    if (telegenceDeviceId == 0 || telegenceDeviceId == null)
                    {
                        LogInfo(context, "EXCEPTION", $"Telegence Device Id can not null or equal 0!");
                        return;
                    }
                    UpdateMobilityDeviceFeature(context, SOCCodes, (int)telegenceDeviceId, id);
                }
            }
            catch (Exception e)
            {
                LogInfo(context, "EXCEPTION", $"Failed to update Mobility Configuration Changes for Queue Id {id}, Message: {e.Message}");
            }
        }

        private void UpdateMobilityDeviceFeature(KeySysLambdaContext context, string SOCCodes, int deviceId, int changeId)
        {
            LogInfo(context, "SUB", $"UpdateMobilityDeviceFeature(SOCCodes: {SOCCodes}, deviceId: {deviceId})");

            using (var Conn = new SqlConnection(context.CentralDbConnectionString))
            {
                using (var cmd = Conn.CreateCommand())
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.CommandText = "dbo.usp_Update_MobilityDeviceFeature";
                    cmd.Parameters.AddWithValue("@SOCCodes", SOCCodes);
                    cmd.Parameters.AddWithValue("@mobilityDeviceId", deviceId);
                    cmd.Parameters.AddWithValue("@processedBy", "UpdateMobilityDeviceFeature_lambda");
                    cmd.Parameters.AddWithValue(CommonSQLParameterNames.CHANGE_ID, changeId);
                    cmd.CommandTimeout = SQLConstant.ShortTimeoutSeconds;
                    Conn.Open();

                    cmd.ExecuteNonQuery();

                    Conn.Close();
                }
            }
        }

        private async Task EnqueueEbondingChangeAsync(KeySysLambdaContext context, IDictionary<string, string> settings, int changeId)
        {
            LogInfo(context, "SUB", $"EnqueueEbondingChangeAsync({{{string.Join(",", settings)}}},{changeId})");
            if (!settings.ContainsKey(EBONDING_CARE_ORDER_REQUEST_QUEUE_URL) || string.IsNullOrWhiteSpace(settings[EBONDING_CARE_ORDER_REQUEST_QUEUE_URL]))
            {
                // so we don't have to enqueue messages during a test
                return;
            }

            using (var client = new AmazonSQSClient(AwsCredentials(context), Amazon.RegionEndpoint.USEast1))
            {
                var request = new SendMessageRequest
                {
                    DelaySeconds = 5,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        {
                            "ChangeId", new MessageAttributeValue {DataType = "Number", StringValue = changeId.ToString()}
                        }
                    },
                    MessageBody = "Not used",
                    QueueUrl = settings[EBONDING_CARE_ORDER_REQUEST_QUEUE_URL]
                };

                var response = await client.SendMessageAsync(request);
                if (((int)response.HttpStatusCode < 200) || ((int)response.HttpStatusCode > 299))
                {
                    LogInfo(context, "EXCEPTION", $"Error enqueuing eBonding change {changeId}: {response.HttpStatusCode:d} {response.HttpStatusCode:g}");
                }
            }
        }

        private void SendResponseToAMOP20(KeySysLambdaContext context, string jobName, string lambdaResponse)
        {
            using (HttpClient client = new HttpClient(new LambdaLoggingHandler()))
            {
                client.BaseAddress = new Uri("https://v1djztyfcg.execute-api.us-east-1.amazonaws.com/dev/migration_job_scheduler");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                string jsonRequest = "{\"data\":{ \"data\": { \"path\": \"/carrier_api_save_to_20\",\"job_name\": \"" + jobName + "\",\"response_message\": \"" + lambdaResponse + "\"}}}";
                var contDevice = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                HttpResponseMessage response = client.PostAsync(client.BaseAddress, contDevice).Result;
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    LogInfo(context, "SUCCESS", "Sent Response to AMOP2.0");
                }
                else
                {
                    var responseBody = response.Content.ReadAsStringAsync().Result;
                    LogInfo(context, "Response Error", responseBody);
                }
            }
        }
    }
}
