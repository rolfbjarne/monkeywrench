/*
 * ReportCommit.aspx.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (RKvinge@novell.com)
 *   
 * Copyright 2009 Novell, Inc. (http://www.novell.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */

#pragma warning disable 649 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.Script.Serialization;

using MonkeyWrench;
using MonkeyWrench.DataClasses;
using MonkeyWrench.Web.WebServices;

public partial class ReportGitHubCommit : System.Web.UI.Page
{
	protected void Page_Load (object sender, EventArgs e)
	{
		string ip = Request.UserHostAddress;
		bool ip_accepted = false;
		string payload;

		Logger.Log ("ReportGitHubCommit.aspx: received post with {2} files from: {0} allowed ips: {1}", ip, Configuration.AllowedCommitReporterIPs, Request.Files.Count);

		foreach (string allowed_ip in Configuration.AllowedCommitReporterIPs.Split ('.')) {
			if (string.IsNullOrEmpty (ip))
				continue;
			if (Regex.IsMatch (ip, FileUtilities.GlobToRegExp (allowed_ip))) {
				ip_accepted = true;
				break;
			}
		}

		if (!ip_accepted) {
			Logger.Log ("ReportGitHubCommit.aspx: {0} tried to send a file, ignored. Allowed IPs: {1}", ip, Configuration.AllowedCommitReporterIPs);
			return;
		}

		payload = Request ["payload"];

		if (!string.IsNullOrEmpty (payload)) {
			if (payload.Length > 1024 * 100) {
				Logger.Log ("ReportGitHubCommit.aspx: {0} tried to send oversized file (> {1} bytes.", Request.UserHostAddress, 1024 * 100);
				return;
			}

			JavaScriptSerializer json = new JavaScriptSerializer ();
			GitHub.Payload pl = json.Deserialize<GitHub.Payload> (payload);
			var repo = pl.repository;

			var hash = new HashSet<string> ();
			Action<string> add_hash = (v) => {
				hash.Add (v);
				if (v.EndsWith (".git"))
					hash.Add (v.Substring (0, v.Length - 4));
				else
					hash.Add (v + ".git");
			};
			add_hash (repo.url);
			add_hash (repo.clone_url);
			add_hash (repo.git_url);
			add_hash (repo.html_url);
			add_hash (repo.ssh_url);

			Logger.Log ("ReportGitHubCommit.aspx: Got 'payload' from {0} with size {1} bytes for repositories {2}", payload.Length, ip, string.Join (", ", hash.ToArray ()));

			WebServices.ExecuteSchedulerForRepositoriesAsync (hash.ToArray ());
		} else {
			Logger.Log ("ReportCommit.aspx: Didn't get a file called 'payload'");
		}

		Response.Write ("OK\n");
		WebServices.ExecuteSchedulerAsync ();
	}

	private class GitHub
	{
		public class Payload
		{
			public string before;
			public string after;
			public string @ref;
			public Commit [] commits;
			public Repository repository;
		}

		public class Commit
		{
			public string id;
			public string message;
			public string timestamp;
			public string url;
			public string [] added;
			public string [] removed;
			public string [] modified;
			public Author author;
		}

		public class Repository
		{
			public string name;
			public string url;
			public string html_url;
			public string git_url;
			public string ssh_url;
			public string clone_url;
			public string pledgie;
			public string description;
			public string homepage;
			public string watchers;
			public string forks;
			public string @private;
			public Owner owner;
		}

		public class Owner
		{
			public string name;
			public string email;
		}

		public class Author
		{
			public string name;
			public string email;
		}
	}
}
