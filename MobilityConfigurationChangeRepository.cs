using System;
using System.Collections.Generic;
using System.Data;
using Amop.Core.Constants;
using Amop.Core.Logger;
using Amop.Core.Models;
using Amop.Core.Resilience;
using Microsoft.Data.SqlClient;
using Polly;

namespace Amop.Core.Repositories
{
    public class MobilityConfigurationChangeRepository
    {
        private const int MaxRetries = 3;
        private readonly string connectionString;
        private readonly ISyncPolicy sqlRetryPolicy;

        public MobilityConfigurationChangeRepository(string connectionString)
            : this(connectionString, new NoOpLogger())
        {
        }

        public MobilityConfigurationChangeRepository(string connectionString, IKeysysLogger logger)
            : this(connectionString, new PolicyFactory(logger))
        {
        }

        public MobilityConfigurationChangeRepository(string connectionString, IPolicyFactory policyFactory)
            : this(connectionString, policyFactory.GetSqlRetryPolicy(MaxRetries))
        {
        }

        public MobilityConfigurationChangeRepository(string connectionString, ISyncPolicy sqlRetryPolicy)
        {
            this.connectionString = connectionString;
            this.sqlRetryPolicy = sqlRetryPolicy;
        }

        public MobilityConfigurationChange GetUnprocessedChange(int changeId)
        {
            return sqlRetryPolicy.Execute(() =>
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    using (var command = new SqlCommand("usp_Get_MobilityConfigurationChangeDetails", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@queueId", changeId);
                        connection.Open();
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return ReadMobilityConfigurationChange(reader);
                            }
                        }
                    }
                }

                return null;
            });
        }

        public void CompleteRatePlanChange(int changeId, string newRatePlanCode, int? optimizationGroupId, DateTime? ratePlanEffectiveDate, Action<string, string> logFunction)
        {
            var parameters = new List<SqlParameter>()
            {
                new SqlParameter(CommonSQLParameterNames.QUEUE_ID, changeId)
            };
            sqlRetryPolicy.Execute(() =>
                Helpers.SqlQueryHelper.ExecuteStoredProcedureWithRowCountResult(logFunction,
                connectionString,
                SQLConstant.StoredProcedureName.UPDATE_MOBILITY_CONFIGURATION_CHANGE_DETAILS,
                parameters,
                SQLConstant.ShortTimeoutSeconds));

            parameters = new List<SqlParameter>()
            {
                new SqlParameter(CommonSQLParameterNames.QUEUE_ID, changeId),
                new SqlParameter(CommonSQLParameterNames.RATE_PLAN, newRatePlanCode)
            };
            if (optimizationGroupId.HasValue)
            {
                parameters.Add(new SqlParameter(CommonSQLParameterNames.OPTIMIZATION_GROUP_ID, optimizationGroupId));
            }
            if (ratePlanEffectiveDate.HasValue)
            {
                parameters.Add(new SqlParameter(CommonSQLParameterNames.RATE_PLAN_EFFECTIVE_DATE, ratePlanEffectiveDate));
            }
            sqlRetryPolicy.Execute(() =>
                Helpers.SqlQueryHelper.ExecuteStoredProcedureWithRowCountResult(logFunction,
                connectionString,
                SQLConstant.StoredProcedureName.UPDATE_MOBILITY_RATE_PLAN_DEVICE_TABLES,
                parameters,
                SQLConstant.ShortTimeoutSeconds));
        }

        private static MobilityConfigurationChange ReadMobilityConfigurationChange(IDataRecord reader)
        {
            return new MobilityConfigurationChange
            {
                MobilityDeviceId = (int)reader["MobilityDeviceId"],
                SubscriberNumber = reader["SubscriberNumber"].ToString(),
                Details = reader["MobilityConfigurationChangeDetails"].ToString(),
                ServiceProviderId = (int)reader["ServiceProviderId"],
                IsProcessed = (bool)reader["IsProcessed"],
                TenantId = reader["TenantId"] != DBNull.Value ? (int)reader["TenantId"] : (int?)null
            };
        }
    }
}
