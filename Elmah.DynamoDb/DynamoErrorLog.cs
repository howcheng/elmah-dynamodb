using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using EL = Elmah;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*
 * To use this in your project, you need to change your web.config or app.config to use this provider.
 * <elmah>
 *     <errorLog type="PNMAC.Core.Elmah.Dynamo.DynamoErrorLog, PNMAC.Core.Elmah.Dynamo" applicationName="APPLICATION_NAME_GOES_HERE" />
 * </elmah>
 * 
 * This provider reads your Amazon credentials and settings from the web.config or app.config file. See http://docs.aws.amazon.com/AWSSdkDocsNET/latest/V3/DeveloperGuide/net-dg-config.html for details.
 * 
 * If you do not have the table ELMAH_Error in your DynamoDB instance, this class will will create it for you.
 * Note that if the table has to be created, it will take a little while before it is ready to be used. 
 * You'll have to manually confirm that the table is in ACTIVE status via your AWS Console or AWS SDK Toolkit.
 */
namespace Elmah.DynamoDb
{
	public class DynamoErrorLog : EL.ErrorLog
	{
		const string TABLE_NAME = "ELMAH_Error";
		private static bool s_TableExists = false;
		private static bool s_CreateTableIfNotExists = false;
		private static string s_TableName = TABLE_NAME;

		static DynamoErrorLog()
		{
			AmazonDynamoDBClient client = new AmazonDynamoDBClient();
			Table errorTable;
			s_TableExists = Table.TryLoadTable(client, s_TableName, out errorTable);
		}

		public DynamoErrorLog(IDictionary config)
		{
			if (config == null)
				throw new ArgumentNullException("config");
			if (!config.Contains("applicationName"))
				throw new InvalidOperationException("'applicationName' attribute missing from Elmah config");
			string appName = (string)config["applicationName"];
			ApplicationName = appName;

			if (config.Contains("tableName"))
				s_TableName = (string)config["tableName"];
		}

		public override EL.ErrorLogEntry GetError(string id)
		{
			CheckTableExists();

			if (string.IsNullOrEmpty(id))
				throw new ArgumentNullException("id");
			Guid errorGuid;
			if (!Guid.TryParse(id, out errorGuid))
				throw new ArgumentException(string.Format("'{0}' is not a Guid", id), "id");

			AmazonDynamoDBClient client = new AmazonDynamoDBClient();
			Table errorTable = Table.LoadTable(client, s_TableName);
			Document errorDoc = errorTable.GetItem(new Primitive(this.ApplicationName), new Primitive(id));
			string errorXml = errorDoc["AllXml"].AsString();
			EL.Error error = EL.ErrorXml.DecodeString(errorXml);
			return new EL.ErrorLogEntry(this, id, error);
		}

		public override int GetErrors(int pageIndex, int pageSize, IList errorEntryList)
		{
			CheckTableExists();

			if (pageIndex < 0)
				throw new ArgumentOutOfRangeException("pageIndex", pageIndex, null);

			if (pageSize < 0)
				throw new ArgumentOutOfRangeException("pageSize", pageSize, null);

			int max = pageSize * (pageIndex + 1);

			// http://docs.aws.amazon.com/amazondynamodb/latest/developerguide/LowLevelDotNetQuerying.html
			AmazonDynamoDBClient client = new AmazonDynamoDBClient();
			Dictionary<string, AttributeValue> lastKeyEvaluated = null;
			List<EL.ErrorLogEntry> list = new List<EL.ErrorLogEntry>(max);
			// there is a max of 1MB of data returned per read operation, so you have to do repeated reads until you reach the end
			do
			{
				Amazon.DynamoDBv2.Model.QueryRequest request = new Amazon.DynamoDBv2.Model.QueryRequest(s_TableName);
				request.KeyConditionExpression = "Application = :v_Application";
				request.ExpressionAttributeValues = new Dictionary<string, AttributeValue> { { ":v_Application", new AttributeValue(this.ApplicationName) } };
				request.IndexName = "Application-TimeUtc-index";
				request.ScanIndexForward = false;
				request.Limit = max;
				request.Select = Select.ALL_PROJECTED_ATTRIBUTES;
				if (lastKeyEvaluated != null)
					request.ExclusiveStartKey = lastKeyEvaluated;

				QueryResponse response = client.Query(request);
				foreach (Dictionary<string, AttributeValue> item in response.Items)
				{
					string errorXml = item["AllXml"].S;
					string errorId = item["ErrorId"].S;
					EL.Error error = EL.ErrorXml.DecodeString(errorXml);
					list.Add(new EL.ErrorLogEntry(this, errorId, error));
				}
				lastKeyEvaluated = response.LastEvaluatedKey;
			} while (lastKeyEvaluated != null && lastKeyEvaluated.Count > 0);

			int numToSkip = (pageIndex - 1) * pageSize;
			list = list.Skip(numToSkip).ToList();
			list.ForEach(err => errorEntryList.Add(err));
			return errorEntryList.Count;
		}

		public override string Log(EL.Error error)
		{
			CheckTableExists();

			string errorXml = EL.ErrorXml.EncodeString(error);
			string id = Guid.NewGuid().ToString();

			ErrorRecord errorToStore = new ErrorRecord
			{
				ErrorId = id,
				Application = this.ApplicationName,
				Host = error.HostName,
				Type = error.Type,
				Source = error.Source,
				Message = error.Message,
				User = error.User,
				StatusCode = error.StatusCode,
				TimeUtc = error.Time.ToUniversalTime().ToString("s"),
				AllXml = errorXml,
			};

			string errorJson = JsonConvert.SerializeObject(errorToStore);
			Document errorDocument = Document.FromJson(errorJson);

			AmazonDynamoDBClient client = new AmazonDynamoDBClient();
			Table errorTable = Table.LoadTable(client, s_TableName);
			errorTable.PutItem(errorDocument);

			return id;
		}

		public override string Name
		{
			get
			{
				return "Amazon DynamoDB Error Log";
			}
		}

		private void CheckTableExists()
		{
			if (!s_TableExists)
			{
				if (s_CreateTableIfNotExists)
					CreateTable();
				else
					throw new InvalidOperationException(string.Format("Table '{0}' does not exist in your DynamoDB instance. Do you have the correct region? If you've just created it, restart your application after the table's status is ACTIVE.", s_TableName));
			}
		}

		private void CreateTable()
		{
			AmazonDynamoDBClient client = new AmazonDynamoDBClient();
			CreateTableRequest request = new CreateTableRequest
			{
				TableName = s_TableName,
				KeySchema = new List<KeySchemaElement> 
					{ 
						new KeySchemaElement("Application", KeyType.HASH),
						new KeySchemaElement("ErrorId", KeyType.RANGE),
					},
				AttributeDefinitions = new List<AttributeDefinition>
					{
						new AttributeDefinition("Application", ScalarAttributeType.S),
						new AttributeDefinition("ErrorId", ScalarAttributeType.S),
						new AttributeDefinition("TimeUtc", ScalarAttributeType.S),
						new AttributeDefinition("AllXml", ScalarAttributeType.S),
					},
				GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
					{
						new GlobalSecondaryIndex
						{
							IndexName = "Application-TimeUtc-index",
							KeySchema = new List<KeySchemaElement>
							{
								new KeySchemaElement("Application", KeyType.HASH),
								new KeySchemaElement("TimeUtc", KeyType.RANGE),
							}
						},
					},
				ProvisionedThroughput = new ProvisionedThroughput(40, 64),
				StreamSpecification = new StreamSpecification
				{
					StreamEnabled = true,
					StreamViewType = StreamViewType.NEW_IMAGE,
				},
			};

			CreateTableResponse response = client.CreateTable(request);
			if ((int)response.HttpStatusCode >= 400)
				throw new AmazonDynamoDBException(string.Format("CreateTable request ID {0} was unsuccessful.", response.ResponseMetadata.RequestId));
		}
	}
}
