﻿/*
 * SchedulerGIT.cs
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
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

using MonkeyWrench.DataClasses;

namespace MonkeyWrench.Scheduler
{
	public class GITUpdater : SchedulerBase
	{
		/* Save a list of fetches done, to not duplicate work when there are several lanes with the same repository */
		private List<string> fetched_directories = new List<string> ();
		private static object commit_lock = new object ();

		class GitEntry
		{
			public string revision;
			public string author;
			public string message;
			public string timestamp;
			public List<string> files;
		}

		public GITUpdater (bool ForceFullUpdate)
			: base (ForceFullUpdate)
		{
		}

		public override void Clear ()
		{
			base.Clear ();

			/* We don't clear fetched_directories here, since it's a per-run variable */
		}

		public override string Type
		{
			get { return "GIT"; }
		}

		protected override int CompareRevisions (string repository, string a, string b)
		{
			throw new NotImplementedException ();
		}

		// a tuple, each path and the first revision (in a list of changesets) the path was modified
		private List<string> paths;
		private List<int> min_revisions;

		protected override void AddChangeSet (XmlDocument doc)
		{
			XmlNode rev = doc.SelectSingleNode ("/monkeywrench/changeset");
			int revision = int.Parse (rev.Attributes ["revision"].Value);
			string root = rev.Attributes ["root"].Value;
			string sc = rev.Attributes ["sourcecontrol"].Value;

			if (sc != "git")
				return;

			if (paths == null) {
				paths = new List<string> ();
				min_revisions = new List<int> ();
			}

			foreach (XmlNode node in doc.SelectNodes ("/monkeywrench/changeset/directories/directory")) {
				Logger.Log ("GIT: Checking changeset directory: '{0}'", node.InnerText);
				string dir = root + "/" + node.InnerText;
				int existing_rev;
				int existing_index = paths.IndexOf (dir);

				if (existing_index > 0) {
					existing_rev = min_revisions [existing_index];
					if (existing_rev > revision)
						continue;
				}
				Logger.Log ("GIT: Added changeset for {1} with path: '{0}'", dir, revision);
				paths.Add (dir);
				min_revisions.Add (revision);
			}
		}

		protected override void UpdateRevisionsInDBInternal (DB db, DBLane lane, string repository, Dictionary<string, DBRevision> revisions, List<DBHost> hosts, List<DBHostLane> hostlanes, string min_revision, string max_revision)
		{
			string revision;
			List<DateTime> used_dates;
			DBRevision r;
			List<GitEntry> log;
			bool only_update_cache;

			Log ("Updating lane: '{0}', repository: '{1}' min_revision: {2} max_revision: {3}", lane.lane, repository, min_revision, max_revision);

			if (string.IsNullOrEmpty (min_revision) && !string.IsNullOrEmpty (lane.min_revision))
				min_revision = lane.min_revision;

			if (string.IsNullOrEmpty (max_revision) && !string.IsNullOrEmpty (lane.max_revision))
				max_revision = lane.max_revision;

			if (string.IsNullOrEmpty (max_revision))
				max_revision = "remotes/origin/master";

			only_update_cache = max_revision == "None";

			log = GetGITLog (lane, repository, min_revision, max_revision, only_update_cache);


			if (only_update_cache) {
				Log ("Only updated cache, not fetching more revisions for '{0}' since max_revision = 'None'", lane.lane);
				return;
			}

			if (log == null || log.Count == 0) {
				Log ("Didn't get a git log for '{0}'", repository);
				return;
			}

			Log ("Got {0} log records", log.Count);

			used_dates = new List<DateTime> ();

			foreach (GitEntry entry in log) {
				string hash = entry.revision;
				string unix_timestamp_str = entry.timestamp;
				long unix_timestamp;
				string author = entry.author;
				string msg = entry.message;
				DateTime date;

				if (!long.TryParse (unix_timestamp_str, out unix_timestamp)) {
					/* here something is wrong, this way the commit shows up as the first one so that it's easy to spot and start investigating */
					date = DateTime.Now.AddYears (20);
					Log ("Could not parse timestamp '{0}' for revision '{1}' in lane '{2}' in repository {3}", unix_timestamp_str, entry.revision, lane.lane, repository);
				} else {
					const long EPOCH_DIFF = 0x019DB1DED53E8000; /* 116444736000000000 nsecs */
					const long RATE_DIFF = 10000000; /* 100 nsecs */
					date = DateTime.FromFileTimeUtc ((unix_timestamp * RATE_DIFF) + EPOCH_DIFF);
				}

				/* 
				 * The timestamp resolution on my machine seems to be 1 second,
				 * which means that if you commit fast enough you'll get
				 * commits with the same date. This is a very bad thing since
				 * the commits are order by the commit date, and if two commits
				 * have the same date the order they're build / shown is random
				 * (the db decides whatever it feels like). Work around this by
				 * keeping a list of used dates and if the date has already
				 * used, add a millisecond to it (and try again). Note that
				 * there is still a possibility of duplicate dates: if there
				 * already is a revision in the database with this date (from
				 * a previous run of the scheduler).
				 * 
				 * It may seem like there is a very small possibility of having
				 * two commits within a second, but this happens all the time
				 * for our test suite.
				 */
				while (used_dates.Contains (date)) {
					date = date.AddMilliseconds (1);
				}
				used_dates.Add (date);

				revision = hash;

				if (revision == null)
					continue;

				if (revisions.ContainsKey (revision)) {
					/* Check if we've saved the wrong date earlier and fix it */
					if (revisions [revision].date > new DateTime (2030, 1, 1)) {
						/* Hopefully this code will not stay here for 20 years */
						revisions [revision].date = date;
						revisions [revision].Save (db);
						Log ("Detected wrong date in revision '{0}' in lane '{1}' in repository {2}, fixing it", revision, lane.lane, repository);
					}
					Log (2, "Already got {0}", revision);
					continue;
				}

				if (!string.IsNullOrEmpty (lane.commit_filter)) {
					FetchFiles (entry, repository);
					if (DoesFilterExclude (entry, lane.commit_filter))
						continue;
				}

				r = new DBRevision ();
				r.revision = revision;
				r.lane_id = lane.id;

				r.author = author;
				if (string.IsNullOrEmpty (r.author)) {
					Log ("No author specified in r{0} in {1}", r.revision, repository);
					r.author = "?";
				}
				r.date = date;
				if (!string.IsNullOrEmpty (msg)) {
					r.log_file_id = db.UploadString (msg, ".log", false).id;
				} else {
					Log ("No msg specified in r{0} in {1}", r.revision, repository);
					r.log_file_id = null;
				}

				r.Save (db);

				Log (1, "Saved revision '{0}' for lane '{1}' author: {2}, date: {3:yyyy/MM/dd HH:mm:ss.ffffff} {5} {6}", r.revision, lane.lane, r.author, r.date, msg, unix_timestamp, unix_timestamp_str);
			}

			return;
		}

		private bool DoesFilterExclude (GitEntry entry, string filter)
		{
			string [] expressions;
			Regex [] regexes;
			bool include_all = false;

			if (string.IsNullOrEmpty (filter))
				return false;

			if (filter.StartsWith ("ExcludeAllExcept:")) {
				include_all = false;
			} else if (filter.StartsWith ("IncludeAllExcept:")) {
				include_all = true;
			} else {
				Log ("Invalid commit filter: {0}, including all commits.", filter);
				return false;
			}

			expressions = filter.Substring (filter.IndexOf (':') + 1).Trim ().Split (';');
			if (expressions.Length > 0) {
				regexes = new Regex [expressions.Length];
				for (int r = 0; r < regexes.Length; r++) {
					regexes [r] = new Regex (FileUtilities.GlobToRegExp (expressions [r].Trim ()));
				}

				for (int f = 0; f < entry.files.Count; f++) {
					for (int r = 0; r < regexes.Length; r++) {
						if (regexes [r].IsMatch (entry.files [f])) {
							return include_all;
						}
					}
				}
			}

			return !include_all;
		}

		private void FetchFiles (GitEntry entry, string repository)
		{
			try {
				string cache_dir = Configuration.GetSchedulerRepositoryCacheDirectory (repository);
				StringBuilder stderr_log = new StringBuilder ();

				entry.files = new List<string> ();

				using (Process git = new Process ()) {
					git.StartInfo.FileName = "git";
					git.StartInfo.Arguments = "show --name-only --pretty='format:' " + entry.revision;
					Log ("Executing: '{0} {1}' in {2}", git.StartInfo.FileName, git.StartInfo.Arguments, cache_dir);
					git.StartInfo.WorkingDirectory = cache_dir;
					git.StartInfo.UseShellExecute = false;
					git.StartInfo.RedirectStandardOutput = true;
					git.StartInfo.RedirectStandardError = true;
					git.StartInfo.WorkingDirectory = cache_dir;

					Thread stdout = new Thread (delegate ()
					{
						string line;
						while ((line = git.StandardOutput.ReadLine ()) != null) {
							if (string.IsNullOrEmpty (line.Trim ()))
								continue;
							entry.files.Add (line);
						}
					});
					Thread stderr = new Thread (delegate ()
					{
						string line;
						while (null != (line = git.StandardError.ReadLine ())) {
							Console.Error.WriteLine (line);
							stderr_log.AppendLine (line);
						}
					});
					git.Start ();
					stdout.Start ();
					stderr.Start ();
					// Wait 10 minutes for git to finish, otherwise abort.
					if (!git.WaitForExit (1000 * 60 * 10)) {
						Log ("Getting files took more than 10 minutes, aborting.");
						try {
							git.Kill ();
							git.WaitForExit (10000); // Give the process 10 more seconds to completely exit.
						} catch (Exception ex) {
							Log ("Aborting file retrieval failed: {0}", ex.ToString ());
						}
					}

					stdout.Join ((int) TimeSpan.FromMinutes (1).TotalMilliseconds);
					stderr.Join ((int) TimeSpan.FromMinutes (1).TotalMilliseconds);

					if (git.HasExited && git.ExitCode == 0) {
						Log ("Got {0} files successfully", entry.files.Count);
					} else {
						Log ("Didn't get files, HasExited: {0}, ExitCode: {1}, stderr: {2}", git.HasExited, git.HasExited ? git.ExitCode.ToString () : "N/A", stderr_log.ToString ());
					}
				}
			} catch (Exception ex) {
				Log ("Exception while trying to get files for commit {1} {0}", ex.ToString (), entry.revision);
			}
		}

		private List<GitEntry> GetGITLog (DBLane dblane, string repository, string min_revision, string max_revision, bool only_update_cache)
		{
			List<GitEntry> result = null;

			try {
				Log ("Retrieving log for '{0}', repository: '{1}', min_revision: {2} max_revision: {3}", dblane.lane, repository, min_revision, max_revision);

				// Updating the repository cache
				string cache_dir = Configuration.GetSchedulerRepositoryCacheDirectory (repository);
				if (!Directory.Exists (cache_dir))
					Directory.CreateDirectory (cache_dir);

				// Download/update the cache
				using (Process git = new Process ()) {
					DateTime git_start = DateTime.Now;
					if (fetched_directories.Contains (repository)) {
						Log ("Not fetching repository '{0}', it has already been fetched in this run", repository);
					} else {
						git.StartInfo.FileName = "git";
						if (!Directory.Exists (Path.Combine (cache_dir, ".git"))) {
							git.StartInfo.Arguments = "clone --no-checkout " + repository + " .";
						} else {
							git.StartInfo.Arguments = "fetch";
						}
						git.StartInfo.WorkingDirectory = cache_dir;
						git.StartInfo.UseShellExecute = false;
						git.StartInfo.RedirectStandardOutput = true;
						git.StartInfo.RedirectStandardError = true;
						Log ("Executing: '{0} {1}' in {2}", git.StartInfo.FileName, git.StartInfo.Arguments, cache_dir);
						git.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
						{
							if (e.Data == null)
								return;
							Log ("FETCH: {0}", e.Data);
						};
						git.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
						{
							if (e.Data == null)
								return;
							Log ("FETCH STDERR: {0}", e.Data);
						};
						git.Start ();
						git.BeginOutputReadLine ();
						git.BeginErrorReadLine ();

						if (!git.WaitForExit (1000 * 60 * 10 /* 10 minutes */)) {
							Log ("Could not fetch repository, git didn't finish in 10 minutes.");
							return null;
						}

						if (!git.HasExited || git.ExitCode != 0) {
							Log ("Could not fetch repository, HasExited: {0}, ExitCode: {1}", git.HasExited, git.HasExited ? git.ExitCode.ToString () : "N/A");
							return null;
						}
						fetched_directories.Add (repository);
						Log ("Fetched git repository in {0} seconds", (DateTime.Now - git_start).TotalSeconds);
					}
				}

				if (only_update_cache)
					return null;

				string range = string.Empty;
				if (string.IsNullOrEmpty (min_revision)) {
					range = max_revision;
				} else {
					range = min_revision + "^.." + max_revision;
				}

				using (Process git = new Process ()) {
					DateTime git_start = DateTime.Now;
					git.StartInfo.FileName = "git";
					// --reverse: git normally gives commits in newest -> oldest, we want to add them to the db in the reverse order
					git.StartInfo.Arguments = "rev-list --reverse --header " + range;
					Log ("Executing: '{0} {1}' in {2}", git.StartInfo.FileName, git.StartInfo.Arguments, cache_dir);
					git.StartInfo.WorkingDirectory = cache_dir;
					git.StartInfo.UseShellExecute = false;
					git.StartInfo.RedirectStandardOutput = true;
					git.StartInfo.RedirectStandardError = true;
					git.StartInfo.WorkingDirectory = cache_dir;

					Thread stdout = new Thread (delegate ()
					{
						StringBuilder builder = new StringBuilder ();
						GitEntry current = new GitEntry ();
						bool in_header = true;
						bool done = false;

						while (!done) {
							int ch = 0;
							if (git.StandardOutput.EndOfStream) {
								done = true;
							} else {
								ch = git.StandardOutput.Read ();
							}

							if (ch == 0) {
								/* end of record */
								if (result == null)
									result = new List<GitEntry> ();
								current.message = builder.ToString ();
								result.Add (current);
								current = new GitEntry ();
								in_header = true;
								builder.Length = 0;
							} else if (in_header && ch == '\n') {
								/* end of header line */
								if (builder.Length == 0) {
									/* entering log message */
									in_header = false;
								} else {
									string header = builder.ToString ();
									if (current.revision == null) {
										current.revision = header;
									} else if (header.StartsWith ("author ")) {
										header = header.Substring ("author ".Length, header.IndexOf ('<') - "author ".Length - 1);
										current.author = header;
									} else if (header.StartsWith ("committer ")) {
										header = header.Substring ("committer ".Length);
										int gt = header.LastIndexOf ('>');
										if (gt > 0) {
											current.timestamp = header.Substring (gt + 1).Trim ();
											current.timestamp = current.timestamp.Substring (0, current.timestamp.IndexOf (' ')).Trim ();
										} else {
											Logger.Log ("Could not find timestamp in committer line");
										}
									} else {
										// do nothing
									}
								}
								builder.Length = 0;
							} else {
								builder.Append ((char) ch);
							}
						}
					});
					Thread stderr = new Thread (delegate ()
					{
						string line;
						while (null != (line = git.StandardError.ReadLine ())) {
							Console.Error.WriteLine (line);
						}
					});
					git.Start ();
					stdout.Start ();
					stderr.Start ();
					// Wait 10 minutes for git to finish, otherwise abort.
					if (!git.WaitForExit (1000 * 60 * 10)) {
						Log ("Getting log took more than 10 minutes, aborting.");
						try {
							git.Kill ();
							git.WaitForExit (10000); // Give the process 10 more seconds to completely exit.
						} catch (Exception ex) {
							Log ("Aborting log retrieval failed: {0}", ex.ToString ());
						}
					}

					stdout.Join ((int) TimeSpan.FromMinutes (1).TotalMilliseconds);
					stderr.Join ((int) TimeSpan.FromMinutes (1).TotalMilliseconds);

					if (git.HasExited && git.ExitCode == 0) {
						Log ("Got log successfully in {0} seconds", (DateTime.Now - git_start).TotalSeconds);
						return result;
					} else {
						Log ("Didn't get log, HasExited: {0}, ExitCode: {1}", git.HasExited, git.HasExited ? git.ExitCode.ToString () : "N/A");
						return null;
					}
				}
			} catch (Exception ex) {
				Log ("Exception while trying to get svn log: {0}", ex.ToString ());
				return null;
			}
		}

		public static void FindPeopleForCommit (DBLane lane, DBRevision revision, List<DBPerson> people)
		{
			DBPerson person;
			try {
				foreach (string repository in lane.repository.Split (new char [] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
					string cache_dir = Configuration.GetSchedulerRepositoryCacheDirectory (repository);

					if (!Directory.Exists (cache_dir))
						continue;

					using (Process git = new Process ()) {
						DateTime git_start = DateTime.Now;
						git.StartInfo.FileName = "git";
						git.StartInfo.Arguments = "log -1 --pretty=format:'%aE%n%aN%n%cE%n%cN' " + revision.revision;
						git.StartInfo.WorkingDirectory = cache_dir;
						git.StartInfo.UseShellExecute = false;
						git.StartInfo.RedirectStandardOutput = true;

						git.Start ();

						string author_email = git.StandardOutput.ReadLine ();
						string author_name = git.StandardOutput.ReadLine ();
						string committer_email = git.StandardOutput.ReadLine ();
						string committer_name = git.StandardOutput.ReadLine ();

						// Wait 10 minutes for git to finish, otherwise abort.
						if (!git.WaitForExit (1000 * 60 * 10)) {
							Logger.Log ("Getting commit info took more than 10 minutes, aborting.");
							try {
								git.Kill ();
								git.WaitForExit (10000); // Give the process 10 more seconds to completely exit.
							} catch (Exception ex) {
								Logger.Log ("Aborting commit info retrieval failed: {0}", ex.ToString ());
							}
						}

						if (git.HasExited && git.ExitCode == 0) {
							Logger.Log ("Got commit info successfully in {0} seconds", (DateTime.Now - git_start).TotalSeconds);
							person = new DBPerson ();
							person.fullname = author_name;
							person.Emails = new string [] { author_email };
							people.Add (person);
							if (author_name != committer_name && !string.IsNullOrEmpty (committer_name)) {
								person = new DBPerson ();
								person.fullname = committer_name;
								person.Emails = new string [] {committer_email};
								people.Add (person);
							}
							Logger.Log ("Git commit info for {0}: author_name = {1} author_email: {2} committer_name: {3} committer_email: {4}", revision.revision, author_name, author_email, committer_name, committer_email);
						} else {
							Logger.Log ("Didn't get commit info, HasExited: {0}, ExitCode: {1}", git.HasExited, git.HasExited ? git.ExitCode.ToString () : "N/A");
						}
					}
				}
			} catch (Exception ex) {
				Logger.Log ("Exception while trying to get commit info: {0}", ex.ToString ());
			}
		}

		public static bool ExecuteTryCommit (DBLane lane, DBTryCommit try_commit, DBRevision revision, StringBuilder log)
		{
			string action;
			string arguments;
			string working_dir;
			string tmp_branch;

			if (string.IsNullOrEmpty (try_commit.branch)) {
				log.AppendLine ("No destination branch specified.");
				return false; 
			}

			try {
				lock (commit_lock) {
					working_dir = Configuration.GetTryCommitCacheDirectory (lane.repository);

					if (!Directory.Exists (working_dir))
						Directory.CreateDirectory (working_dir);

					// Download/update the cache
					action = "fetch/update repository";
					if (!Directory.Exists (Path.Combine (working_dir, ".git"))) {
						arguments = "clone --no-checkout " + lane.repository + " .";
					} else {
						arguments = "fetch";
					}
					if (!Git (working_dir, arguments, log, action))
						return false;

					// Checkout a local branch tracking the remote destination branch
					action = "checkout local branch";
					tmp_branch = string.Format ("try-commit-branch-{0}", DateTime.Now.Ticks);
					arguments = string.Format ("checkout --force -b {0} --track remotes/origin/{1}", tmp_branch, try_commit.branch);
					if (!Git (working_dir, arguments, log, action))
						return false;

					// Do the actual cherry-pick/merge
					switch (try_commit.SuccessfulAction) {
					case DBTryCommitAction.CherryPick:
						action = "cherry pick";
						arguments = string.Format ("cherry-pick {0}", revision.revision);
						break;
					case DBTryCommitAction.Merge:
						action = "merge";
						arguments = string.Format ("merge {0}", revision.revision);
						break;
					default:
						log.AppendLine ("Unknown action: " + try_commit.SuccessfulAction);
						return false;
					}
					if (!Git (working_dir, arguments, log, action))
						return false;

					// Push to remote server
					action = "push";
					arguments = string.Format ("push origin HEAD:{0}", try_commit.branch);
					if (!Git (working_dir, arguments, log, action))
						return false;

					// Move to another branch
					action = "checkout another branch";
					arguments = "checkout master";
					if (!Git (working_dir, arguments, log, action))
						return false;

					// Delete the branch we created
					action = "delete temporary branch";
					arguments = "branch -D " + tmp_branch;
					if (!Git (working_dir, arguments, log, action))
						return false;
				}
				return true;
			} catch (Exception ex) {
				log.AppendLine (ex.ToString ());
				return false;
			}
		}

		private static bool Git (string working_dir, string arguments, StringBuilder log, string action)
		{
			using (Process git = new Process ()) {
				DateTime git_start = DateTime.Now;
				git.StartInfo.FileName = "git";
				git.StartInfo.Arguments = arguments;
				git.StartInfo.WorkingDirectory = working_dir;
				git.StartInfo.UseShellExecute = false;
				git.StartInfo.RedirectStandardOutput = true;
				git.StartInfo.RedirectStandardError = true;
				log.AppendFormat ("Executing " + action + ": '{0} {1}' in {2}\n", git.StartInfo.FileName, git.StartInfo.Arguments, working_dir);
				git.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
				{
					if (e.Data == null)
						return;
					log.AppendFormat ("STDOUT: {0}\n", e.Data);
				};
				git.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
				{
					if (e.Data == null)
						return;
					log.AppendFormat ("STDERR: {0}\n", e.Data);
				};
				git.Start ();
				git.BeginOutputReadLine ();
				git.BeginErrorReadLine ();

				if (!git.WaitForExit (1000 * 60 * 10 /* 10 minutes */)) {
					log.AppendFormat ("Could not " + action + ", git didn't finish in 10 minutes.\n");
					return false;
				}

				if (!git.HasExited || git.ExitCode != 0) {
					log.AppendFormat ("Could not " + action + ", HasExited: {0}, ExitCode: {1}\n", git.HasExited, git.HasExited ? git.ExitCode.ToString () : "N/A");
					return false;
				}

				log.AppendFormat ("Completed " + action + " in {0} seconds.\n", (DateTime.Now - git_start).TotalSeconds);
			}

			return true;
		}
	}
}
