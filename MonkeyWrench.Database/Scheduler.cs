/*
 * Scheduler.cs
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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

using MonkeyWrench.Database;
using MonkeyWrench.DataClasses;

namespace MonkeyWrench.Scheduler
{
	public static class Scheduler
	{
		private static bool is_executing;

		public static bool IsExecuting
		{
			get { return is_executing; }
		}
	
		public static void Main (string [] args)
		{
			ProcessHelper.Exit (Main2 (args)); // Work around #499702
		}

		public static int Main2 (string [] args)
		{
			if (!Configuration.LoadConfiguration (args))
				return 1;

			return ExecuteScheduler (false) ? 0 : 1;
		}

		public static void ExecuteSchedulerAsync (bool forcefullupdate)
		{
			Async.Execute (delegate (object o) 
			{
				ExecuteScheduler (forcefullupdate); 
			});
		}

		public static List<XmlDocument> GetReports (bool forcefullupdate)
		{
			List<XmlDocument> result = null;
			if (!Configuration.ForceFullUpdate && !forcefullupdate) {
				try {
					foreach (string file in Directory.GetFiles (Configuration.GetSchedulerCommitsDirectory (), "*.xml")) {
						string hack = File.ReadAllText (file);
						if (!hack.Contains ("</directory>"))
							hack = hack.Replace ("</directory", "</directory>");
						File.WriteAllText (file, hack);
						XmlDocument doc = new XmlDocument ();
						try {
							Logger.Log ("Updater: got report file '{0}'", file);
							doc.Load (file);
							if (result == null)
								result = new List<XmlDocument> ();
							result.Add (doc);
						} catch (Exception ex) {
							Logger.Log ("Updater: exception while checking commit report '{0}': {1}", file, ex);
						}
						try {
							File.Delete (file); // No need to check this file more than once.
						} catch {
							// Ignore any exceptions
						}
					}
				} catch (Exception ex) {
					Logger.Log ("Updater: exception while checking commit reports: {0}", ex);
				}
			}
			return result;
		}

		public static bool ExecuteScheduler (bool forcefullupdate)
		{
			Lock scheduler_lock = null;
			List<DBLane> lanes;

			List<DBHost> hosts;
			List<DBHostLane> hostlanes;
			List<XmlDocument> reports;
			
			try {
				scheduler_lock = Lock.Create ("MonkeyWrench.Scheduler");
				if (scheduler_lock == null) {
					Logger.Log ("Could not aquire scheduler lock.");
					return false;
				}

				Logger.Log ("Scheduler lock aquired successfully.");
				
				is_executing = true;

				SVNUpdater.StartDiffThread ();

				// Check reports
				reports = GetReports (forcefullupdate);

				using (DB db = new DB (true)) {
					lanes = db.GetAllLanes ();
					hosts = db.GetHosts ();
					hostlanes = db.GetAllHostLanes ();
					Logger.Log ("Updater will now update {0} lanes.", lanes.Count);
					foreach (DBLane lane in lanes) {
						SchedulerBase updater;
						switch (lane.source_control) {
						case "svn":
							updater = new SVNUpdater (forcefullupdate);
							break;
						case "git":
							updater = new GITUpdater (forcefullupdate);
							break;
						default:
							Logger.Log ("Unknown source control: {0} for lane {1}", lane.source_control, lane.lane);
							continue;
						}
						updater.AddChangeSets (reports);
						updater.UpdateRevisionsInDB (db, lane, hosts, hostlanes);
						UpdateBuildLogDB (db, lane, hosts, hostlanes);
					}
				}

				Logger.Log ("Update done, waiting for diff thread to finish...");

				SVNUpdater.StopDiffThread ();

				Logger.Log ("Update finished successfully.");

				return true;
			} catch (Exception ex) {
				Logger.Log ("An exception occurred: {0}", ex.ToString ());
				return false;
			} finally {
				if (scheduler_lock != null)
					scheduler_lock.Unlock ();
				is_executing = false;
			}
		}

		/// <summary>
		/// Returns true if something was added to the database.
		/// </summary>
		/// <param name="db"></param>
		/// <param name="lane"></param>
		/// <param name="host"></param>
		/// <returns></returns>
		public static bool AddRevisionWork (DB db, DBLane lane, DBHost host)
		{
			StringBuilder sql = new StringBuilder ();
			int line_count = 0;

			Logger.Log ("AddRevisionWork ({0} (id: {1}), {2} (id: {3}))", lane.lane, lane.id, host.host, host.id);

			try {
				using (IDbCommand cmd = db.CreateCommand ()) {
					cmd.CommandText = @"
SELECT Lane.id AS lid, Revision.id AS rid, Host.id AS hid
FROM Host, Lane
INNER JOIN Revision ON Revision.lane_id = lane.id
WHERE 
	Lane.id = @lane_id AND 
	Host.id = @host_id AND
	NOT EXISTS (
		SELECT 1
		FROM RevisionWork 
		WHERE RevisionWork.lane_id = Lane.id AND RevisionWork.host_id = Host.id AND RevisionWork.revision_id = Revision.id
		);
";
					DB.CreateParameter (cmd, "lane_id", lane.id);
					DB.CreateParameter (cmd, "host_id", host.id);
					using (IDataReader reader = cmd.ExecuteReader ()) {
						while (reader.Read ()) {
							int lane_id = reader.GetInt32 (reader.GetOrdinal ("lid"));
							int host_id = reader.GetInt32 (reader.GetOrdinal ("hid"));
							int revision_id = reader.GetInt32 (reader.GetOrdinal ("rid"));
							line_count++;
							sql.AppendFormat ("INSERT INTO RevisionWork (lane_id, host_id, revision_id) VALUES ({0}, {1}, {2});", lane_id, host_id, revision_id);
						}
					}
				}
				if (line_count > 0) {
					Logger.Log ("AddRevisionWork: Adding {0} records.", line_count);
					db.ExecuteScalar (sql.ToString ());
				} else {
					Logger.Log ("AddRevisionWork: Nothing to add.");
				}
				Logger.Log ("AddRevisionWork [Done]");
				return line_count > 0;
			} catch (Exception ex) {
				Logger.Log ("AddRevisionWork got an exception: {0}\n{1}", ex.Message, ex.StackTrace);
				return false;
			}
		}

		private static void UpdateBuildLogDB (DB db, DBLane lane, List<DBHost> hosts, List<DBHostLane> hostlanes)
		{
			List<DBRevision> revisions;
			List<DBCommand> commands = null;
			List<DBLaneDependency> dependencies = null;
			DBHostLane hostlane;
			DBWork work;
			bool got_dependencies = false;
			bool got_commands = false;

			try {
				Logger.Log ("Updating build db log... Got {0} hosts", hosts.Count);
				foreach (DBHost host in hosts) {
					hostlane = null;
					for (int i = 0; i < hostlanes.Count; i++) {
						if (hostlanes [i].lane_id == lane.id && hostlanes [i].host_id == host.id) {
							hostlane = hostlanes [i];
							break;
						}
					}
					if (hostlane == null) {
						Logger.Log ("Lane '{0}' is not configured for host '{1}', not adding any work.", lane.lane, host.host);
						continue;
					} else if (!hostlane.enabled) {
						Logger.Log ("Lane '{0}' is disabled for host '{1}', not adding any work.", lane.lane, host.host);
						continue;
					}

					AddRevisionWork (db, lane, host);

					revisions = db.GetDBRevisionsWithoutWork (lane.id, host.id);

					Logger.Log ("Updating build db log... Got {0} revisions for host {1}", revisions.Count, host.host);

					foreach (DBRevision revision in revisions) {
						bool dependencies_satisfied = true;

						if (!got_commands) {
							commands = db.GetCommands (lane.id);
							got_commands = true;
						}

						if (!got_dependencies) {
							dependencies = DBLaneDependency_Extensions.GetDependencies (db, lane);
							got_dependencies = true;
						}

						if (dependencies != null) {
							Logger.Log ("Lane '{0}', revision '{1}' checking dependencies...", lane.lane, revision.revision);

							foreach (DBLaneDependency dependency in dependencies)
								dependencies_satisfied &= dependency.IsSuccess (db, revision.revision);

							Logger.Log ("Lane '{0}', revision '{1}' dependency checking resulted in: {2}.", lane.lane, revision.revision, dependencies_satisfied);
						}

						int revisionwork_id;
						bool pending_dependencies;

						using (IDbCommand cmd = db.CreateCommand ()) {
							cmd.CommandText = "SELECT add_revisionwork (@lane_id, @host_id, @revision_id);";
							DB.CreateParameter (cmd, "lane_id", lane.id);
							DB.CreateParameter (cmd, "host_id", host.id);
							DB.CreateParameter (cmd, "revision_id", revision.id);
							revisionwork_id = (int) cmd.ExecuteScalar ();
						}

						using (IDbCommand cmd = db.CreateCommand ()) {
							cmd.CommandText = "SELECT state FROM RevisionWork WHERE id = @id;";
							DB.CreateParameter (cmd, "id", revisionwork_id);
							pending_dependencies = (int) DBState.DependencyNotFulfilled == (int) cmd.ExecuteScalar ();
						}

						if (pending_dependencies && !dependencies_satisfied)
							continue;

						Logger.Log ("Pending dependencies: {0}", pending_dependencies);

						foreach (DBCommand command in commands) {
							work = null;
							if (pending_dependencies) {
								using (IDbCommand cmd = db.CreateCommand ()) {
									cmd.CommandText = "SELECT * FROM Work WHERE revisionwork_id = @revisionwork_id AND command_id = @command_id;";
									DB.CreateParameter (cmd, "revisionwork_id", revisionwork_id);
									DB.CreateParameter (cmd, "command_id", command.id);
									using (IDataReader reader = cmd.ExecuteReader ()) {
										if (reader.Read ())
											work = new DBWork (reader);
									}
								}
							}

							if (work == null) {
								work = new DBWork ();
								work.command_id = command.id;
								work.revisionwork_id = revisionwork_id;
							}
							work.State = dependencies_satisfied ? DBState.NotDone : DBState.DependencyNotFulfilled;
							work.Save (db);
							Logger.Log ("Saved revision {0}, host {2}, command {1}", revision.revision, command.command, host.host);
						}

						if (!dependencies_satisfied) {
							db.ExecuteScalar (string.Format ("UPDATE RevisionWork SET state = 9 WHERE id = {0} AND state = 0;", revisionwork_id));
						} else {
							db.ExecuteScalar (string.Format ("UPDATE RevisionWork SET state = 0 WHERE id = {0} AND state = 9;", revisionwork_id));
						}
					}
				}
			} catch (Exception ex) {
				Logger.Log ("There was an exception while updating build db: {0}", ex.ToString ());
			}
			Logger.Log ("Updating build db log... [Done]");
		}

	}
}
