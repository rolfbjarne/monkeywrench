﻿/*
 * ViewWorkTable.aspx.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (RKvinge@novell.com)
 *   
 * Copyright 2009 Novell, Inc. (http://www.novell.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

using MonkeyWrench.DataClasses;
using MonkeyWrench.DataClasses.Logic;
using MonkeyWrench.Web.WebServices;

public partial class ViewWorkTable : System.Web.UI.Page
{
	private new Master Master
	{
		get { return base.Master as Master; }
	}

	protected override void OnLoad (EventArgs e)
	{
		base.OnLoad (e);

		try {
			DBHost host;
			DBLane lane;
			DBCommand command = null;
			GetViewWorkTableDataResponse response;

			response = Master.WebService.GetViewWorkTableData (Master.WebServiceLogin,
				Utils.TryParseInt32 (Request ["lane_id"]), Request ["lane"],
				Utils.TryParseInt32 (Request ["host_id"]), Request ["host"],
				Utils.TryParseInt32 (Request ["command_id"]), Request ["command"]);

			lane = response.Lane;
			host = response.Host;
			command = response.Command;

			if (lane == null || host == null || command == null)
				Response.Redirect ("index.aspx");

			header.InnerHtml = GenerateHeader (response, lane, host, command);
			buildtable.InnerHtml = GenerateLane (response, lane, host, command);
		} catch (Exception ex) {
			Response.Write (ex.ToString ().Replace ("\n", "<br/>"));
		}
	}
	public string GenerateHeader (GetViewWorkTableDataResponse response, DBLane lane, DBHost host, DBCommand command)
	{
		if (!Utils.IsInRole (MonkeyWrench.DataClasses.Logic.Roles.Administrator)) {
			return string.Format (@"
<h2>Step {4} on lane '{2}' on '{3}' (<a href='ViewTable.aspx?lane_id={0}&amp;host_id={1}'>table</a>)</h2><br/>", lane.id, host.id, lane.lane, host.host, command.command);
		} else {
			return string.Format (@"
<h2>Step {4} on lane '<a href='EditLane.aspx?lane_id={0}'>{2}</a>' on '<a href='EditHost.aspx?host_id={1}'>{3}</a>' 
(<a href='ViewTable.aspx?lane_id={0}&amp;host_id={1}'>table</a>)</h2><br/>", lane.id, host.id, lane.lane, host.host, command.command);
		}
	}

	public string GenerateLane (GetViewWorkTableDataResponse response, DBLane lane, DBHost host, DBCommand command)
	{
		StringBuilder matrix = new StringBuilder ();
		List<DBWorkView2> steps;
		DateTime beginning = new DateTime (2001, 1, 1, 0, 0, 0);

		steps = response.WorkViews;

		matrix.AppendLine ("<table class='buildstatus'>");
		matrix.AppendLine ("<tr>");
		matrix.AppendLine ("\t<th>Revision</th>");
		matrix.AppendLine ("\t<th>Start Time</th>");
		matrix.AppendLine ("\t<th>Duration</th>");;
		matrix.AppendLine ("\t<th>Html report</th>");
		matrix.AppendLine ("\t<th>Summary</th>");
		matrix.AppendLine ("\t<th>Files</th>");
		matrix.AppendLine ("</tr>");


		for (int i = 0; i < steps.Count; i++) {
			DBWorkView2 view = steps [i];
			List<DBWorkFileView> files = response.WorkFileViews [i];
			DBState state = (DBState) view.state;

			matrix.Append ("<tr>");

			// revision
			string result;
			switch (state) {
			case DBState.NotDone:
				result = "queued"; break;
			case DBState.Executing:
				result = "running"; break;
			case DBState.Failed:
				result = view.nonfatal ? "issues" : "failure"; break;
			case DBState.Success:
			case DBState.Aborted:
			case DBState.Timeout:
			case DBState.Paused:
			default:
				result =state.ToString ().ToLowerInvariant ();
				break;
			}

			// result

			int file_id = 0;
			foreach (DBWorkFileView file in files) {
				if (!file.filename.StartsWith (view.command))
					continue;
				file_id = file.id;
				break;
			}
			if (file_id == 0) {
				matrix.AppendFormat ("\t<td class='{0}'>{1}</td>", result, view.revision);
			} else {
				matrix.AppendFormat ("\t<td class='{0}'><a href='GetFile.aspx?id={2}'>{1}</a></td>", result, view.revision, file_id);
			}

			if (state > DBState.NotDone && state != DBState.Paused) {
				matrix.AppendFormat ("<td>{0}</td>", view.starttime.ToString ("yyyy/MM/dd HH:mm:ss UTC"));
			} else {
				matrix.AppendLine ("<td>-</td>");
			}
			// duration
			DateTime starttime = view.starttime.ToLocalTime ();
			DateTime endtime = view.endtime.ToLocalTime ();
			int duration = (int) (endtime - starttime).TotalSeconds;
			matrix.Append ("\t<td>");
			if (state >= DBState.Executing && state != DBState.Paused) {
				matrix.Append ("[");
				matrix.Append (TimeSpan.FromSeconds (duration).ToString ());
				matrix.Append ("]");
			} else {
				matrix.Append ("-");
			}
			matrix.AppendLine ("</td>");

			// html report
			matrix.AppendLine ("<td>");
			DBWorkFileView index_html = null;
			foreach (DBWorkFileView file in files) {
				if (file.filename == "index.html") {
					index_html = file;
					break;
				}
			}
			if (index_html != null) {
				matrix.AppendFormat ("<a href='ViewHtmlReport.aspx?workfile_id={0}'>View html report</a>", index_html.id);
			} else {
				matrix.AppendLine ("-");
			}
			matrix.AppendLine ("</td>");

			// summary
			matrix.AppendLine ("<td>");
			matrix.AppendLine (view.summary);
			matrix.AppendLine ("</td>");


			matrix.AppendLine ("</tr>");
		}
		
		matrix.AppendLine ("</table>");
		
		return matrix.ToString ();
	}
}
