# Elmah.DynamoDb
This provider connects ELMAH in your .NET application to an instance of DynamoDB in Amazon Web Services (AWS) as a data store. Please note this has not yet been tested in a production environment. I wrote this for my work, but we are far from actually using it in the wild. Bug notices and fixes are welcome.

# Usage

## Configure Elmah.DynamoDb

    <elmah>
        <errorLog type="Elmah.DynamoDb.DynamoErrorLog, Elmah.DynamoDb" applicationName="YOUR APPLICATION NAME" tableName="ELMAH_Error" createTableIfNotExists="false" />
    </elmah>

* `tableName` is optional. It will default to `ELMAH_Error` if you do not set it.
* `createTableIfNotExists`, when set to true, will cause Elmah.DynamoDb to create the table and the requisite indexes on your behalf.

## Configure AWS

Elmah.DynamoDb reads your AWS settings from your application's main configuration (web.config or app.config). See http://docs.aws.amazon.com/AWSSdkDocsNET/latest/V3/DeveloperGuide/net-dg-config.html for details. Whatever identity you use to connect to the table must the following access:
* GetItem
* PutItem
* Query

If you want Elmah.DynamoDb to create the table for you, then you will need to give it CreateTable and UpdateTable access as well.

## ELMAH_Error table

This table mimics the ELMAH_Error table that the ELMAH SQL Server provider uses. You can create the table yourself, or let the component do it for you. If you want to do it yourself, here is what you'll need:

1. The table's name must match the `tableName` attribute in the config, or `ELMAH_Error` if you use the default.
2. The primary key should be a HASH-RANGE type.
  1. The HASH attribute name is `Application` and is a string.
  2. The RANGE attribute is `ErrorId` and is a string.
3. Create a secondary global index, also of HASH-RANGE type, named `Application-TimeUtc-index`.
  1. The HASH attribute is `Application`.
  2. The RANGE attribute `TimeUtc` and is a string.
  3. The projected attributes for this index should be: `Application`, `ErrorId`, `TimeUtc`, `AllXml`
4. Provisioned read and write capacity units are 8 and 32 (tweak as necessary).

