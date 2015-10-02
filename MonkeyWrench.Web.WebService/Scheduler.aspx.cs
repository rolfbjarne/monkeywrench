/*
 * Scheduler.aspx.cs
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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Web;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;

using MonkeyWrench;
using MonkeyWrench.DataClasses;
using MonkeyWrench.DataClasses.Logic;
using MonkeyWrench.Web.WebServices;
using MonkeyWrench.Scheduler;

namespace MonkeyWrench.WebServices
{
	public class JsonResponse {
		public SchedulerQueue Queue;
		public Dictionary<string, List<string>> Repositories;
	}
	public partial class Scheduler : System.Web.UI.Page
	{
		protected void Page_Load (object sender, EventArgs e)
		{
			Dictionary<string, List<string>> repositories = new Dictionary<string, List<string>> ();

			using (var db = new DB (true)) {
				var login = Authentication.CreateLogin (Request);
				try {
					Authentication.VerifyUserInRole (Context, db, login, Roles.Administrator, true);
				} catch (UnauthorizedException) {
					Response.StatusCode = (int) System.Net.HttpStatusCode.Forbidden;
					Response.Status = "403 FORBIDDEN";
					Response.Clear ();
					return;
				}

				using (var cmd = db.CreateCommand ()) {
					cmd.CommandText = "SELECT repository, lane FROM Lane;";
					using (var reader = cmd.ExecuteReader ()) {
						while (reader.Read ()) {
							var repo = reader.GetString (0);
							var lane = reader.GetString (1);
							List<string> lanes;
							if (!repositories.TryGetValue (repo, out lanes))
								repositories [repo] = lanes = new List<string> ();
							lanes.Add (lane);
						}
					}
				}
			}

			var queue = MonkeyWrench.Scheduler.Scheduler.Queue;
			var working_repos = new Dictionary<string, ScheduledUpdate> ();
			var waiting_repos = new Dictionary<string, ScheduledUpdate> ();

			foreach (var update in queue.Working)
				waiting_repos.Add (update.Repository.Repository, update);
			
			foreach (var update in queue.Waiting)
				waiting_repos.Add (update.Repository.Repository, update);

			var s = new System.Web.Script.Serialization.JavaScriptSerializer ();
			var rv = new JsonResponse ();
			rv.Queue = queue;
			rv.Repositories = repositories;
			Response.Write (s.Serialize (rv));
			return;

//			int i = 0;
//			foreach (var repodata in repositories) {
//				var repo = repodata.Key;
//				var lanes = repodata.Value;
//				var state = string.Empty;
//				ScheduledUpdate working_update = null;
//				ScheduledUpdate waiting_update = null;
//				working_repos.TryGetValue (repo, out working_update);
//				waiting_repos.TryGetValue (repo, out waiting_update);
//
//				if (working_update != null) {
//					state += "Working";
//				}
//				if (waiting_update != null) {
//					if (state != string.Empty)
//						state += "; ";
//					state += "Enqueued";
//				}
//
//				i++;
//
//				var content = new HtmlGenericControl ("div");
//				content.ID = "content" + i.ToString ();
//				content.Style.Add (System.Web.UI.HtmlTextWriterStyle.Display, "none");
//				content.Style.Add (System.Web.UI.HtmlTextWriterStyle.PaddingLeft, "20px");
//
//				content.Controls.Add (
//					new HyperLink () {
//						NavigateUrl = string.Format ("javascript:enqueueRepo ('{0}', true);", repo),
//						Text = "Enqueue (full update)"
//					});
//				content.Controls.Add (new HtmlGenericControl ("br"));
//				content.Controls.Add (new HyperLink () { NavigateUrl = string.Format ("javascript:enqueueRepo ('{0}', false);", repo), Text = "Enqueue" });
//
//				var sb = new StringBuilder ();
//				sb.Append ("Lanes: ");
//				lanes.Sort ();
//				for (int l = 0; l < lanes.Count; l++) {
//					if (l > 0)
//						sb.Append (", ");
//					sb.Append (lanes [l]);
//				}
//				content.Controls.Add (new HtmlGenericControl ("br"));
//				content.Controls.Add (new HtmlGenericControl ("span") { InnerText = sb.ToString () });
//
//				var log = new HtmlGenericControl ("div");
//				log.ID = "log" + i.ToString ();
//				log.Style.Add (System.Web.UI.HtmlTextWriterStyle.Display, "none");
//
//				var div = new HtmlGenericControl ("div");
//				div.ID = "repo" + i.ToString ();
//				div.InnerText = repo + (string.IsNullOrEmpty (state) ? string.Empty : " (" + state + ")");
//				div.Attributes.Add ("onclick", "javascript:toggleRepo ('" + content.ID + "');");
//
//				mainDiv.Controls.Add (div);
//				mainDiv.Controls.Add (content);
//				mainDiv.Controls.Add (log);
//
//			}

			// working
			// waiting
			// statistics:
			// updates per repository (simple, full)
		}
	}
}
