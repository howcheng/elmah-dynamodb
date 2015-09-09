using EL = Elmah;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Specialized;

namespace Elmah.DynamoDb
{
	/// <summary>
	/// This class mimics the ELMAH_Errors SQL table structure for ELMAH errors. It gets serialized to JSON for conversion to a <see cref="Amazon.DynamoDBv2.DocumentModel.Document"/> object.
	/// Differences between the table and this class:
	/// * ErrorId is a string instead of a UNIQUEIDENTIFER
	/// * TimeUtc is a string instead of a DATETIME
	/// </summary>
	internal class ErrorRecord
	{
		public string ErrorId { get; set; }
		public string Application { get; set; }
		public string Host { get; set; }
		public string Type { get; set; }
		public string Source { get; set; }
		public string Message { get; set; }
		public string User { get; set; }
		public int StatusCode { get; set; }
		/// <summary>
		/// Timestamp of the error in ISO 8601 format (yyyy-mm-ddThh:mm:ss)
		/// </summary>
		public string TimeUtc { get; set; }
		public string AllXml { get; set; }
	}
}
