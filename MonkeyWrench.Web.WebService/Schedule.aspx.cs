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
	public partial class Schedule : System.Web.UI.Page
	{
		protected void Page_Load (object sender, EventArgs e)
		{
			try {
			bool stream_log = true;
			string repo = null;
			int? lane_id = null;
			bool forcefullupdate = false;

			Logger.Log ("Requesting stuff?");
			repo = Request ["repo"];
			if (!string.IsNullOrEmpty (Request ["lane_id"])) {
				int tmp;
				if (Request ["lane_id"] == "all") {
					lane_id = int.MaxValue;
				} else if (!int.TryParse (Request ["lane_id"], out tmp)) {
					Response.Write (string.Format ("Invalid lane id: {0}", Request ["lane_id"]));
					return;
				} else {
					lane_id = tmp;
				}
			}
			if (!string.IsNullOrEmpty (Request ["stream_log"])) {
				if (!bool.TryParse (Request ["stream_log"], out stream_log)) {
					Response.Write (string.Format ("Invalid value '{0}' for parameter 'stream_log'", Request ["stream_log"]));
					return;
				}
			}

			if (!string.IsNullOrEmpty (Request ["forcefullupdate"])) {
				if (!bool.TryParse (Request ["forcefullupdate"], out forcefullupdate)) {
					Response.Write (string.Format ("Invalid value '{0}' for parameter 'forcefullupdate'", Request ["forcefullupdate"]));
					return;
				}
			}

			if (string.IsNullOrEmpty (repo) && !lane_id.HasValue) {
				Logger.Log ("Requesting nothing");
				Response.Write ("Either 'repo' or 'lane_id' must be given.");
				return;
			}


			ResponseLogger logger = null;

			if (stream_log) {
				Response.ContentType = "application/octet-stream";
				logger = new ResponseLogger (Response);
			}
			Logger.Log ("Requesting stuff repo: {0}", repo);
			try {
				using (DB db = new DB ()) {
					var login = Authentication.CreateLogin (Request);
					Authentication.VerifyUserInRole (Context, db, login, Roles.Administrator, true);

					if (stream_log) {
						var evt = new ManualResetEvent (false);
						// Run the scheduler in a separate thread, so that ASP.NET doesn't terminate
						// it if the request times out.
						ThreadPool.QueueUserWorkItem ((v) => {
							try {
								if (lane_id.HasValue) {
									if (lane_id.Value == int.MaxValue) {
										MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerSync (logger, forcefullupdate);
									} else {
										MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerSync (logger, forcefullupdate, lane_id.Value);
									}
								} else if (!string.IsNullOrEmpty (repo)) {
									MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerSync (logger, forcefullupdate, repo);
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
					} else {
						if (lane_id.HasValue) {
							if (lane_id.Value == int.MaxValue) {
//								MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerAsync ();
							} else {
//								MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerAsync (lane_id.Value);
							}
						} else if (string.IsNullOrEmpty (repo)) {
							MonkeyWrench.Scheduler.Scheduler.ExecuteSchedulerAsync (new string [] { repo });
						}
						Response.Write ("OK");
					}
				}
			} catch (Exception ex) {
				if (logger != null)
					logger.ClearResponse ();
				Response.Write (string.Format ("An exception occurred: {0}\n", ex.Message));
				Response.Flush ();
				HttpContext.Current.ApplicationInstance.CompleteRequest ();
			} finally {
				if (logger != null)
					logger.ClearResponse ();
			}
		} catch (Exception ex) {
			Logger.Log ("ops " + ex.ToString ());
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
}
