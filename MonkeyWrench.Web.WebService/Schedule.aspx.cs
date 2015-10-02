/*
 * ScheduleLane.aspx.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (rolf@xamarin.com)
 *   
 * Copyright 2015 Xamarin Inc (http://www.xamarin.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */

#pragma warning disable 649 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Web;

using MonkeyWrench;
using MonkeyWrench.DataClasses;
using MonkeyWrench.DataClasses.Logic;
using MonkeyWrench.Web.WebServices;
using MonkeyWrench.Scheduler;

namespace MonkeyWrench.WebServices
{
	public partial class ScheduleLane : System.Web.UI.Page
	{
		protected void Page_Load (object sender, EventArgs e)
		{
			int lane_id;

			if (Request ["lane_id"] == "all") {
				lane_id = int.MaxValue;
			} else if (!int.TryParse (Request ["lane_id"], out lane_id)) {
				Response.Write (string.Format ("Invalid lane id: {0}", Request ["lane_id"]));
				return;
			}

			Response.ContentType = "application/octet-stream";

			var logger = new ResponseLogger (Response);
			try {
				using (DB db = new DB ()) {
					var login = Authentication.CreateLogin (Request);
					Authentication.VerifyUserInRole (Context, db, login, Roles.Administrator, true);

					var evt = new ManualResetEvent (false);
					// Run the scheduler in a separate thread, so that ASP.NEt doesn't terminate
					// it if the request times out.
					ThreadPool.QueueUserWorkItem ((v) => {
						try {
							if (lane_id == int.MaxValue) {
								MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerSync (logger);
							} else {
								MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerSync (lane_id, logger);
							}
						} catch (ThreadAbortException) {
						} catch (Exception ex) {
							logger.Log ("Exception occurred: {0}", ex);
							Logger.Log ("Exception occurred: {0}", ex);
						} finally {
							evt.Set ();
						}
					});
					evt.WaitOne ();
				}
			} catch (Exception ex) {
				logger.ClearResponse ();
				Response.Write (string.Format ("An exception occurred: {0}\n", ex.Message));
				Response.Flush ();
				HttpContext.Current.ApplicationInstance.CompleteRequest ();
			} finally {
				logger.ClearResponse ();
			}
		}
	}

	class ResponseLogger : ILogger {
		HttpResponse response;
		object lock_obj = new object ();

		public ResponseLogger (HttpResponse response)
		{
			this.response = response;
		}

		public void ClearResponse ()
		{
			lock (lock_obj) {
				response = null;
			}
		}

		public void Log (string format, params object[] args)
		{
			try {
				lock (lock_obj) {
					if (response != null) {
						response.Write (Logger.FormatLog (format, args));
						response.Flush ();
					}
				}
			} catch {
				// Ignore any exceptions here (for instance if the response is closed), so that
				// the update isn't aborted by spurious exceptions.
			}
		}

		public void LogRaw (string message)
		{
			try {
				lock (lock_obj) {
					if (response != null) {
						response.Write (message);
						response.Flush ();
					}
				}
			} catch {
				// Ignore any exceptions here (for instance if the response is closed), so that
				// the update isn't aborted by spurious exceptions.
			}
		}

		public void LogTo (string logName, string format, params object[] args)
		{
			Log (format, args);
		}

		public void LogToRaw (string logName, string message)
		{
			LogRaw (message);
		}
	}
}
