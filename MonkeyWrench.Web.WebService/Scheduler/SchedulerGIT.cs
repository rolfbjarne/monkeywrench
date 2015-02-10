/*
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
	class GITUpdater : SchedulerBase
	{
		class GitEntry
		{
			public string revision;
			public string author;
			public string message;
			public string timestamp;
		}

		HashSet<string> repositories;

		public GITUpdater (HashSet<string> repositories, bool forceFullUpdate)
			: base (forceFullUpdate)
		{
			this.repositories = repositories;
		}

		public void UpdateRevisionsInDB (DB db, DBLane lane, List<DBHost> hosts, List<DBHostLane> hostlanes, ILogger log)
		{
			bool update_steps = false;
			string [] min_revisions;
			string [] max_revisions;
			string [] repositories;

			log.Log ("Updating '{0}', ForceFullUpdate: {1}", lane.lane, ForceFullUpdate);

			try {
				repositories = lane.repository.Split (new char [] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				min_revisions = splitWithMinimumElements (lane.min_revision, repositories.Length);
				max_revisions = splitWithMinimumElements (lane.max_revision, repositories.Length);
				log.Log ("Updating {0} repositories", repositories.Length);
				for (int i = 0; i < repositories.Length; i++) {
					UpdateRevisionsInDBInternal (log, db, lane, repositories [i], hosts, hostlanes, min_revisions [i], max_revisions [i]);
				}

				log.Log ("Updating db for lane '{0}'... [Done], update_steps: {1}", lane.lane, update_steps);
			} catch (Exception ex) {
				log.Log ("There was an exception while updating db for lane '{0}': {1}", lane.lane, ex.ToString ());
			}
		}

		protected void UpdateRevisionsInDBInternal (ILogger logger, DB db, DBLane lane, string repository, List<DBHost> hosts, List<DBHostLane> hostlanes, string min_revision, string max_revision)
		{
			string revision;
			var used_dates = new List<DateTime> ();
			List<GitEntry> log;
			var sql = new StringBuilder ();

			if (string.IsNullOrEmpty (max_revision))
				max_revision = "remotes/origin/master";

			logger.Log ("Updating lane: '{0}', repository: '{1}' min revision: '{2}' max revision: '{3}'", lane.lane, repository, min_revision, max_revision);

			log = GetGITLog (logger, lane, repository, min_revision, max_revision);

			if (log == null || log.Count == 0) {
				logger.Log ("Didn't get a git log for '{0}'", repository);
				return;
			}

			logger.Log ("Got {0} log records", log.Count);

			using (var cmd = db.CreateCommand ()) {
				foreach (GitEntry entry in log) {
					string hash = entry.revision;
					string unix_timestamp_str = entry.timestamp;
					long unix_timestamp;
					string author = entry.author;
					DateTime date;

					if (!long.TryParse (unix_timestamp_str, out unix_timestamp)) {
						/* here something is wrong, this way the commit shows up as the first one so that it's easy to spot and start investigating */
						date = DateTime.Now.AddYears (20);
						logger.Log ("Could not parse timestamp '{0}' for revision '{1}' in lane '{2}' in repository {3}", unix_timestamp_str, entry.revision, lane.lane, repository);
						continue;
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

					sql.AppendFormat ("INSERT INTO Revision (lane_id, revision, author, date) SELECT {0}, '{1}', @author_{1}, @date_{1} WHERE NOT EXISTS (SELECT revision FROM Revision WHERE revision = '{1}');\n",
						lane.id, revision);
					DB.CreateParameter (cmd, "@author_" + revision, author ?? "?");
					DB.CreateParameter (cmd, "@date_" + revision, date);

					lane.last_revision = revision;
				}

				sql.AppendFormat ("UPDATE Lane SET last_revision = '{0}' where id = {1};\n", lane.last_revision, lane.id);

				cmd.CommandText = sql.ToString ();

				try {
					int rv = cmd.ExecuteNonQuery ();
					logger.Log ("Updated {0} records of {1} logs (+ 1 lane update)", rv - 1, log.Count);
				} catch (Exception ex) {
					logger.Log ("Failed to execute sql query: {0}\n{1}", ex.Message, cmd.CommandText);
					throw;
				}
			}

		}

		public Dictionary<string, MemoryLogger> FetchGitRepositories (ILogger log)
		{
			var rv = new Dictionary<string, MemoryLogger> ();
			var evt = new CountdownEvent (repositories.Count);
			var watch = new Stopwatch ();

			watch.Start ();
			log.Log ("Fetching {0} repositories", repositories.Count);

			foreach (var repo in repositories) {
				// Fetch git repositories in parallel.
				// Don't use the threadpool since that may starve incoming web requests.
				ParameterizedThreadStart cmd = (obj) =>
				{
					try {
						var rlog = new MemoryLogger ();
						var mlog = new AggregatedLogger (log, rlog);
						try {
							FetchGitRepository (repo, mlog);
						} catch (Exception ex) {
							log.Log ("Exception while fetching git repository {0}: {1}", repo, ex);
						}
						lock (rv)
							rv.Add (repo, rlog);
					} finally {
						evt.Signal ();
					}
				};
				if (repositories.Count > 1) {
					var t = new Thread (cmd);
					t.IsBackground = true;
					t.Start (repo);
				} else {
					cmd (repo);
				}
			}
			if (!evt.Wait (TimeSpan.FromMinutes (60))) {
				log.Log ("Failed to fetch {0} repositories in 60 minutes.", repositories.Count);
			} else {
				log.Log ("Fetched {0} repositories in {1} seconds", repositories.Count, watch.Elapsed.TotalSeconds);
			}
			return rv;
		}

		static void FetchGitRepository (string repository, ILogger log)
		{
			// Updating the repository cache
			string cache_dir = Configuration.GetSchedulerRepositoryCacheDirectory (repository);
			if (!Directory.Exists (cache_dir))
				Directory.CreateDirectory (cache_dir);

			// Download/update the cache
			using (Process git = new Process ()) {
				var watch = new Stopwatch ();
				watch.Start ();
				git.StartInfo.FileName = "git";
				int timeout = 10;
				if (!Directory.Exists (Path.Combine (cache_dir, ".git"))) {
					git.StartInfo.Arguments = "clone --progress --no-checkout " + repository + " .";
					timeout = 60; // clones can be *slow*.
				} else {
					git.StartInfo.Arguments = "fetch --progress";
				}
				git.StartInfo.WorkingDirectory = cache_dir;
				git.StartInfo.UseShellExecute = false;
				git.StartInfo.RedirectStandardOutput = true;
				git.StartInfo.RedirectStandardError = true;
				git.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
				git.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
				log.Log ("Fetching git repository: '{0} {1}' in {2}", git.StartInfo.FileName, git.StartInfo.Arguments, cache_dir);
				git.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
				{
					if (e.Data == null)
						return;
					lock (log)
						log.Log ("STDOUT for {1}: {0}", e.Data, repository);
				};
				git.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
				{
					if (e.Data == null)
						return;
					lock (log)
						log.Log ("STDERR for {1}: {0}", e.Data, repository);
				};
				git.Start ();
				git.BeginOutputReadLine ();
				git.BeginErrorReadLine ();

				if (!git.WaitForExit (1000 * 60 * timeout /* 60 minutes */)) {
					log.Log ("Could not fetch repository {0}, git didn't finish in {1} minutes.", repository, timeout);
					return;
				}

				if (!git.HasExited || git.ExitCode != 0) {
					log.Log ("Could not fetch repository {2}, HasExited: {0}, ExitCode: {1}", git.HasExited, git.HasExited ? git.ExitCode.ToString () : "N/A", repository);
					return;
				}
				log.Log ("Fetched git repository {1} in {0} seconds", watch.Elapsed.TotalSeconds, repository);
			}
		}

		private List<GitEntry> GetGITLog (ILogger log, DBLane dblane, string repository, string min_revision, string max_revision)
		{
			List<GitEntry> result = null;

			try {
				log.Log ("Retrieving log for '{0}', repository: '{1}', min_revision: {2} max_revision: {3}", dblane.lane, repository, min_revision, max_revision);

				// Updating the repository cache
				string cache_dir = Configuration.GetSchedulerRepositoryCacheDirectory (repository);
				if (!Directory.Exists (cache_dir))
					Directory.CreateDirectory (cache_dir);

				if (!ForceFullUpdate && !string.IsNullOrEmpty (dblane.last_revision)) {
					min_revision = dblane.last_revision;
					log.Log ("Using last revision {0} for {1}", min_revision, dblane.lane);
				}

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
					git.StartInfo.Arguments = "rev-list --reverse --header ";
					if (!dblane.traverse_merge)
						git.StartInfo.Arguments += "--first-parent ";
					git.StartInfo.Arguments += range;
					log.Log ("Executing: '{0} {1}' in {2}", git.StartInfo.FileName, git.StartInfo.Arguments, cache_dir);
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
								
								if (current.revision != null)
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
											log.Log ("Could not find timestamp in committer line");
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
							log.Log ("STDERR for {0}: {1}", repository, line);
						}
					});
					git.Start ();
					stdout.Start ();
					stderr.Start ();
					// Wait 10 minutes for git to finish, otherwise abort.
					if (!git.WaitForExit (1000 * 60 * 10)) {
						log.Log ("Getting log took more than 10 minutes, aborting.");
						try {
							git.Kill ();
							git.WaitForExit (10000); // Give the process 10 more seconds to completely exit.
						} catch (Exception ex) {
							log.Log ("Aborting log retrieval failed: {0}", ex.ToString ());
						}
					}

					stdout.Join ((int) TimeSpan.FromMinutes (1).TotalMilliseconds);
					stderr.Join ((int) TimeSpan.FromMinutes (1).TotalMilliseconds);

					if (git.HasExited && git.ExitCode == 0) {
						log.Log ("Got log successfully in {0} seconds", (DateTime.Now - git_start).TotalSeconds);
						return result;
					} else {
						log.Log ("Didn't get log, HasExited: {0}, ExitCode: {1}", git.HasExited, git.HasExited ? git.ExitCode.ToString () : "N/A");
						return null;
					}
				}
			} catch (Exception ex) {
				log.Log ("Exception while trying to get git log: {0}", ex.ToString ());
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

	}
}
