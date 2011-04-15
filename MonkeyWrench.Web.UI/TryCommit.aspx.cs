/*
 * TryCommit.aspx.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (RKvinge@novell.com)
 *   
 * Copyright 2011 Novell, Inc. (http://www.novell.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */

using System;
using System.Collections.Generic;
using System.Threading;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

using MonkeyWrench;
using MonkeyWrench.DataClasses;
using MonkeyWrench.DataClasses.Logic;
using MonkeyWrench.Web.WebServices;

public partial class TryCommit : System.Web.UI.Page
{
	bool text_output;

	private new Master Master
	{
		get { return base.Master as Master; }
	}

	private void Report (string message)
	{
		Logger.Log ("TryCommit.Report ({0}) text output: {1}", message, text_output);
		if (!text_output) {
			lblMessage.Text = message;
		} else {
			Response.Clear ();
			Response.Write (HttpUtility.HtmlDecode (message));
			Response.Flush ();
			Response.Close ();
			Context.ApplicationInstance.CompleteRequest ();
		}
		Logger.Log ("TryCommit.Report ({0}) text output: {1} [Done]", message, text_output);
	}

	protected override void OnLoad (EventArgs e)
	{
		string output = Request ["output"];

		base.OnLoad (e);

		text_output = !string.IsNullOrEmpty (output) && output == "text";

		Logger.Log ("TryCommit.OnLoad () text output: {0}", text_output);

		try {
			GetLanesResponse lanes;
			string lane = Request ["lane"];
			string commit = Request ["commit"];
			string branch = Request ["branch"];
			int? action = null;
			int? lane_id = null;
			int tmp;

			if (int.TryParse (Request ["lane_id"], out tmp))
				lane_id = tmp;
			if (int.TryParse (Request ["action"], out tmp))
				action = tmp;

			if (text_output) {
				Master.WebServiceLogin.Password = Request ["password"];
				Master.WebServiceLogin.User = Request ["user"];
			}

			if (!string.IsNullOrEmpty (branch))
				txtBranch.Text = branch;
			if (!string.IsNullOrEmpty (commit))
				txtCommit.Text = commit;
			if (action.HasValue)
				lstActions.SelectedIndex = action.Value;
			if (lane_id.HasValue || !string.IsNullOrEmpty (lane)) {
				lstLanes.Items.Add (new ListItem (string.IsNullOrEmpty (lane) ? lane_id.Value.ToString () : lane, lane_id.HasValue ? lane_id.Value.ToString () : "-1"));

				cmdTry_Click (cmdTry, null);
				if (text_output)
					return;
			} else {
				lanes = Master.WebService.GetLanes (Master.WebServiceLogin);
				if (lanes.Exception != null) {
					Report (Utils.FormatException (lanes.Exception.Message));
					return;
				}

				foreach (DBLane l in lanes.Lanes) {
					lstLanes.Items.Add (new ListItem (l.lane, l.id.ToString ()));
					if (l.lane.Contains ("try") && lstLanes.SelectedItem == null)
						lstLanes.SelectedIndex = lstLanes.Items.Count - 1;
				}
			}

			cmdTry.Attributes ["onclick"] = "tryCommit ('" + lstLanes.ClientID + "', '" + lstActions.ClientID + "', '" + txtBranch.ClientID + "', '" + txtCommit.ClientID + "');";
		} catch (ThreadAbortException) {
			throw;
		} catch (Exception ex) {
			Report (Utils.FormatException (ex, true));
		}
	}

	void cmdTry_Click (object sender, EventArgs e)
	{
		AddTryCommitResponse response;

		try {
			int? lane_id;
			string lane;
			string revision;
			int successful_action;
			string branch;

			if (lstLanes.SelectedItem == null) {
				throw new Exception ("You must select a lane");
			}

			lane_id = int.Parse (lstLanes.SelectedItem.Value);
			if (lane_id == -1)
				lane_id = null;
			lane = lstLanes.SelectedItem.Text;
			revision = txtCommit.Text;
			branch = txtBranch.Text;
			successful_action = int.Parse (lstActions.SelectedItem.Value);

			response = Master.WebService.AddTryCommit (Master.WebServiceLogin, lane_id, lane, revision, successful_action, branch);
			if (response.Exception != null) {
				Report (Utils.FormatException (response.Exception.Message));
				return;
			}

			foreach (DBHost host in response.Hosts) {
				lblMessage.Text += string.Format ("Added for host {0}\n", host.host);
			}

			if (response.Hosts.Count == 1) {
				if (!text_output) {
					Response.Redirect (string.Format ("ViewLane.aspx?lane_id={0}&host_id={1}&revision={2}", lane_id, response.Hosts [0].id, revision), false);
				}
			}
			Report (lblMessage.Text);
		} catch (ThreadAbortException) {
			throw;
		} catch (Exception ex) {
			Report (Utils.FormatException (ex, true));
		}
	}
}
