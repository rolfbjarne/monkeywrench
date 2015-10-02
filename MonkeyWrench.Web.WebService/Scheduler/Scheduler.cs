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
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

using MonkeyWrench.Database;
using MonkeyWrench.DataClasses;
using MonkeyWrench.WebServices;

namespace MonkeyWrench.Scheduler
{
	public static class Scheduler
	{
		const string logName = "scheduler.log";
		static object lock_obj = new object ();
		static bool queued_forced = false;
		static HashSet<string> queued_repositories = new HashSet<string> ();
		static object queue_lock = new object ();
		static ScheduledState state = new ScheduledState ();
		static SchedulerQueue queue = new SchedulerQueue ();
		static Semaphore semaphore;

		public static SchedulerQueue Queue {
			get {
				return queue;
			}
		}

		public static void ExecuteSchedulerAsync (bool forcefullupdate)
		{
			Async.Execute (delegate (object o) 
			{
				ExecuteScheduler (forcefullupdate); 
			});
		}

		public static void ExecuteSchedulerAsync (string[] repositories)
		{
			Async.Execute ((v) => {
				Enqueue (null, false, repositories, null);
			});
		}

		static void WaitForAll (IEnumerable<ScheduledUpdate> updates)
		{
			foreach (var update in updates)
				update.WaitForCompletion ();
		}

		public static void ExecuteSchedulerSync (ILogger logger, bool forcefullupdate)
		{
			WaitForAll (Enqueue (logger, forcefullupdate, null, null));
		}

		public static void ExecuteSchedulerSync (ILogger logger, bool forcefullupdate, int lane_id)
		{
			WaitForAll (Enqueue (logger, forcefullupdate, null, new int [] { lane_id }));
		}

		public static void ExecuteSchedulerSync (ILogger logger, bool forcefullupdate, string repo)
		{
			WaitForAll (Enqueue (logger, forcefullupdate, new string[] { repo }, null));
		}

		public static void ExecuteScheduler (bool forcefullupdate)
		{
			Enqueue (null, forcefullupdate, null, null);
		}

		static void ProcessUpdate (ScheduledUpdate update)
		{
			Stopwatch watch = new Stopwatch ();
			List<DBLane> lanes;
			List<DBHost> hosts;
			List<DBHostLane> hostlanes;

			try {
				watch.Start ();

				using (DB db = new DB (true)) {
					lanes = db.GetLanesForRepository (update.Repository.Repository);

					if (lanes.Count == 0) {
						update.Log.Log ("There are no lanes for the repository '{0}'", update.Repository);
						return;
					}

					hosts = db.GetHosts ();
					hostlanes = db.GetAllHostLanes ();

					var filtered_lanes = new List<DBLane> ();
					foreach (var lane in lanes) {
						if (!lane.enabled) {
							update.Log.Log ("Skipping disabled lane '{0}'", lane.lane);
							continue;
						}

						if (!hostlanes.Exists ((hl) => hl.lane_id == lane.id && hl.enabled)) {
							update.Log.Log ("Skipping lane {0}, not enabled or configured on any host.", lane.lane);
							continue;
						}

						if (update.FilterToLanes != null && !update.FilterToLanes.Contains (lane.id)) {
							update.Log.Log ("Skipping lane {0}, it's not selected for update.", lane.lane);
							continue;
						}

						filtered_lanes.Add (lane);
					}

					if (filtered_lanes.Count == 0) {
						update.Log.Log ("There are no lanes left for the repository '{0}'", update.Repository);
						return;
					}

					update.Log.Log ("Scheduler will process {0} lanes: {1}", filtered_lanes.Count, string.Join (", ", filtered_lanes.ToArray ().Select ((l) => l.lane)));

					var updater = new GITUpdater (update);
					updater.FetchGitRepository ();

					foreach (DBLane lane in filtered_lanes)
						updater.UpdateRevisionsInDB (db, lane);

					AddRevisionWork (db, update.Log);
					AddWork (db, hosts, filtered_lanes, lanes, hostlanes, update.Log);
					if (update.FullUpdate)
						CheckDependencies (db, hosts, lanes, hostlanes, update.Log);
				}

				update.Log.Log ("Scheduler finished successfully in {0} seconds.", watch.Elapsed.TotalSeconds);
			} catch (Exception ex) {
				update.Log.Log ("An exception occurred in the scheduler: {0}", ex.ToString ());
			}
		}

		static void Schedule (object update_obj)
		{
			ScheduledUpdate update = (ScheduledUpdate) update_obj;
			try {
				ProcessUpdate (update);
			} catch (Exception ex) {
				Logger.LogTo (logName, "Scheduler exception: {0}", ex);
			} finally {
				queue.CompleteWork (update);
			}
		}

		public static void Start ()
		{
			var t = new Thread (ScheduleLoop);
			t.IsBackground = true;
			t.Start ();
		}

		static ScheduledUpdate [] Enqueue (ILogger extra_log, bool forcefullupdate, string[] repos, int[] filter_to_lanes)
		{
			IEnumerable<string> reps = repos;

			if (reps == null) {
				var repositories = new List<string> ();
				using (var db = new DB (true)) {
					using (var cmd = db.CreateCommand ()) {
						cmd.CommandText = "SELECT DISTINCT repository FROM Lane WHERE enabled = TRUE;";
						using (var reader = cmd.ExecuteReader ()) {
							while (reader.Read ()) {
								repositories.Add (reader.GetString (0));	
							}
						}
					}
				}
				reps = repositories;
			}

			var lst = new List<ScheduledUpdate> ();
			foreach (var repo in reps) {
				var update = new ScheduledUpdate (repo, forcefullupdate, filter_to_lanes, extra_log);
				lst.Add (update);
				AddUpdate (update);
			}

			Logger.LogTo (logName, "Enqueued {0} updates (full: {1}, repositories: {2} filter_to_lanes: {3})",
				lst.Count,
				forcefullupdate,
				string.Join (", ", reps),
				filter_to_lanes == null ? "N/A" : string.Join (", ", filter_to_lanes.Select (lane_id => lane_id.ToString ()).ToArray ()));

			return lst.ToArray ();
		}

		static void AddUpdate (ScheduledUpdate update)
		{
			queue.Enqueue (update);
		}

		static void ScheduleLoop ()
		{
			Lock scheduler_lock = null;

			try {
				while ((scheduler_lock = MonkeyWrench.Lock.Create ("MonkeyWrench.Scheduler")) == null) {
					Logger.LogTo (logName, "Could not aquire lock 'MonkeyWrench.Scheduler'. Will try again in 15 seconds.");
					Thread.Sleep (TimeSpan.FromSeconds (15));
				}

				// When wrench launches, there will typically be a horde of
				// bots trying to connect, so delay the scheduler for a minute
				// so that we can deal with the bots first.
				Logger.LogTo (logName, "Scheduler started with max {0} threads. Waiting 1 minute for wrench to launch.", Configuration.MaxSchedulerThreads);
//				Thread.Sleep (TimeSpan.FromMinutes (1));

				semaphore = new Semaphore (Configuration.MaxSchedulerThreads, Configuration.MaxSchedulerThreads);

				Logger.LogTo (logName, "Scheduler startup wait complete. Will now process updates.");
				while (true) {
					try {
						ScheduledUpdate update = queue.FetchWork ();
						try {
							semaphore.WaitOne ();
							var t = new Thread (Schedule);
							t.IsBackground = true;
							t.Start (update);
						} finally {
							semaphore.Release ();
						}
					} catch (Exception ex) {
						Logger.LogTo (logName, "Exception in scheduler thread: {0}", ex);
					}
				}
			} finally {
				if (scheduler_lock != null)
					scheduler_lock.Unlock ();
			}
		}

		/// <summary>
		/// Returns true if something was added to the database.
		/// </summary>
		/// <param name="db"></param>
		/// <param name="lane"></param>
		/// <param name="host"></param>
		/// <returns></returns>
		static void AddRevisionWork (DB db, ILogger log)
		{
			var stopwatch = new Stopwatch ();
			stopwatch.Start ();
			int line_count = 0;

			try {
				using (var cmd = db.CreateCommand (@"
					INSERT INTO RevisionWork (lane_id, host_id, revision_id, state)
					SELECT Lane.id, Host.id, Revision.id, 10
					FROM HostLane
					INNER JOIN Host ON HostLane.host_id = Host.id
					INNER JOIN Lane ON HostLane.lane_id = Lane.id
					INNER JOIN Revision ON Revision.lane_id = lane.id
					WHERE HostLane.enabled = true AND
						NOT EXISTS (
							SELECT 1
							FROM RevisionWork 
							WHERE RevisionWork.lane_id = Lane.id AND RevisionWork.host_id = Host.id AND RevisionWork.revision_id = Revision.id
							)
					RETURNING lane_id, host_id, revision_id
				"))
				using (IDataReader reader = cmd.ExecuteReader ()) {
					while (reader.Read ()) {
						int lane_id = reader.GetInt32 (0);
						int host_id = reader.GetInt32 (1);
						int revision_id = reader.GetInt32 (2);

						var info = new GenericNotificationInfo(); 
						info.laneID = lane_id;
						info.hostID = host_id;
						info.revisionID = revision_id;
						info.message = "Commit received.";
						info.state = DBState.Executing;

						Notifications.NotifyGeneric (info);

						line_count++;
					}
				}
				log.Log ("AddRevisionWork: Added {0} records", line_count);
			} catch (Exception ex) {
				log.Log ("AddRevisionWork got an exception: {0}\n{1}", ex.Message, ex.StackTrace);
			} finally {
				stopwatch.Stop ();
				log.Log ("AddRevisionWork done in {0} seconds", stopwatch.Elapsed.TotalSeconds);
			}
		}

		static void CollectWork (List<DBCommand> commands_in_lane, List<DBLane> lanes, DBLane lane, List<DBCommand> commands)
		{
			while (lane != null) {
				commands_in_lane.AddRange (commands.Where (w => w.lane_id == lane.id));
				lane = lanes.FirstOrDefault ((v) => lane.parent_lane_id == v.id);
			}
		}

		private static void AddWork (DB db, List<DBHost> hosts, List<DBLane> selected_lanes, List<DBLane> all_lanes, List<DBHostLane> hostlanes, ILogger log)
		{
			var watch = new Stopwatch ();
			watch.Start ();
			List<DBCommand> commands = null;
			List<DBLaneDependency> dependencies = null;
			List<DBCommand> commands_in_lane;
			List<DBRevisionWork> revisionwork_without_work = new List<DBRevisionWork> ();
			DBHostLane hostlane;
			StringBuilder sql = new StringBuilder ();
			bool fetched_dependencies = false;
			int lines = 0;

			try {
				/* Find the revision works which don't have work yet */
				using (IDbCommand cmd = db.CreateCommand ()) {
					cmd.CommandText = "SELECT * FROM RevisionWork WHERE state = 10;";
					using (IDataReader reader = cmd.ExecuteReader ()) {
						while (reader.Read ()) {
							revisionwork_without_work.Add (new DBRevisionWork (reader));
						}
					}
				}

				log.Log ("AddWork: Got {0} hosts and {1} revisionwork without work", hosts.Count, revisionwork_without_work.Count);

				foreach (DBLane lane in selected_lanes) {
					var laneLog = Configuration.GetLogFileForLane (lane.id);
					commands_in_lane = null;

					foreach (DBHost host in hosts) {
						hostlane = null;
						for (int i = 0; i < hostlanes.Count; i++) {
							if (hostlanes [i].lane_id == lane.id && hostlanes [i].host_id == host.id) {
								hostlane = hostlanes [i];
								break;
							}
						}

						if (hostlane == null) {
//							log.LogTo (laneLog, "AddWork: Lane '{0}' is not configured for host '{1}', not adding any work.", lane.lane, host.host);
							continue;
						} else if (!hostlane.enabled) {
							log.LogTo (laneLog, "AddWork: Lane '{0}' is disabled for host '{1}', not adding any work.", lane.lane, host.host);
							continue;
						}

						log.LogTo (laneLog, "AddWork: Lane '{0}' is enabled for host '{1}', adding work!", lane.lane, host.host);

						foreach (DBRevisionWork revisionwork in revisionwork_without_work) {
							bool has_dependencies;

							/* revisionwork_without_work contains rw for all hosts/lanes, filter out the ones we want */
							if (revisionwork.host_id != host.id || revisionwork.lane_id != lane.id)
								continue;

							/* Get commands and dependencies for all lanes only if we know we'll need them */
							if (commands == null)
								commands = db.GetCommands (0);
							if (commands_in_lane == null) {
								commands_in_lane = new List<DBCommand> ();
								CollectWork (commands_in_lane, all_lanes, lane, commands);
							}

							if (!fetched_dependencies) {
								fetched_dependencies = true;
								dependencies = DBLaneDependency_Extensions.GetDependencies (db, null);
							}

							has_dependencies = dependencies != null && dependencies.Any (dep => dep.lane_id == lane.id);

//							log.LogTo (laneLog, "AddWork: Lane '{0}', revisionwork_id '{1}' has dependencies: {2}", lane.lane, revisionwork.id, has_dependencies);

							foreach (DBCommand command in commands_in_lane) {
								int work_state = (int) (has_dependencies ? DBState.DependencyNotFulfilled : DBState.NotDone);

								sql.AppendFormat ("INSERT INTO Work (command_id, revisionwork_id, state) VALUES ({0}, {1}, {2});\n", command.id, revisionwork.id, work_state);
								lines++;

//								log.LogTo (laneLog, "Lane '{0}', revisionwork_id '{1}' Added work for command '{2}'", lane.lane, revisionwork.id, command.command);

								if ((lines % 100) == 0) {
									db.ExecuteNonQuery (sql.ToString ());
									sql.Clear ();
									log.LogTo (laneLog, "AddWork: flushed work queue, added {0} items now.", lines);
								}
							}

							sql.AppendFormat ("UPDATE RevisionWork SET state = {0} WHERE id = {1} AND state = 10;", (int) (has_dependencies ? DBState.DependencyNotFulfilled : DBState.NotDone), revisionwork.id);
						}
					}
				}
				if (sql.Length > 0)
					db.ExecuteNonQuery (sql.ToString ());
			} catch (Exception ex) {
				log.Log ("AddWork: There was an exception while adding work: {0}", ex.ToString ());
			}
			log.Log ("AddWork: [Done in {0} seconds]", watch.Elapsed.TotalSeconds);
		}

		private static void CheckDependenciesSlow (DB db, List<DBHost> hosts, List<DBLane> lanes, List<DBHostLane> hostlanes, List<DBLaneDependency> dependencies)
		{
			DateTime start = DateTime.Now;
			//List<DBRevision> revisions = new List<DBRevision> ();
			//List<DBCommand> commands = null;
			//IEnumerable<DBLaneDependency> dependencies_in_lane;
			//IEnumerable<DBCommand> commands_in_lane;
			//List<DBRevisionWork> revisionwork_without_work = new List<DBRevisionWork> ();
			//DBHostLane hostlane;
			//StringBuilder sql = new StringBuilder ();

			try {
				Logger.Log ("CheckDependenciesSlow: IMPLEMENTED, BUT NOT TESTED");
				return;

				//Logger.Log (1, "CheckDependenciesSlow: Checking {0} dependencies", dependencies.Count);

				///* Find the revision works which has unfulfilled dependencies */
				//using (IDbCommand cmd = db.CreateCommand ()) {
				//    cmd.CommandText = "SELECT * FROM RevisionWork WHERE state = 9;";
				//    using (IDataReader reader = cmd.ExecuteReader ()) {
				//        while (reader.Read ()) {
				//            revisionwork_without_work.Add (new DBRevisionWork (reader));
				//        }
				//    }
				//}

				//Logger.Log (1, "CheckDependencies: Got {0} revisionwork with unfulfilled dependencies", revisionwork_without_work.Count);

				//foreach (DBLane lane in lanes) {
				//    dependencies_in_lane = null;
				//    commands_in_lane = null;

				//    foreach (DBHost host in hosts) {
				//        hostlane = null;
				//        for (int i = 0; i < hostlanes.Count; i++) {
				//            if (hostlanes [i].lane_id == lane.id && hostlanes [i].host_id == host.id) {
				//                hostlane = hostlanes [i];
				//                break;
				//            }
				//        }

				//        if (hostlane == null) {
				//            Logger.Log (2, "CheckDependencies: Lane '{0}' is not configured for host '{1}', not checking dependencies.", lane.lane, host.host);
				//            continue;
				//        } else if (!hostlane.enabled) {
				//            Logger.Log (2, "CheckDependencies: Lane '{0}' is disabled for host '{1}', not checking dependencies.", lane.lane, host.host);
				//            continue;
				//        }

				//        Logger.Log (1, "CheckDependencies: Lane '{0}' is enabled for host '{1}', checking dependencies...", lane.lane, host.host);

				//        foreach (DBRevisionWork revisionwork in revisionwork_without_work) {
				//            bool dependencies_satisfied = true;

				//            /* revisionwork_without_work contains rw for all hosts/lanes, filter out the ones we want */
				//            if (revisionwork.host_id != host.id || revisionwork.lane_id != lane.id)
				//                continue;

				//            /* Get commands and dependencies for all lanes only if we know we'll need them */
				//            if (commands == null)
				//                commands = db.GetCommands (0);
				//            if (commands_in_lane == null)
				//                commands_in_lane = commands.Where (w => w.lane_id == lane.id);
				//            if (dependencies == null)
				//                dependencies = DBLaneDependency_Extensions.GetDependencies (db, null);
				//            if (dependencies_in_lane == null)
				//                dependencies_in_lane = dependencies.Where (dep => dep.lane_id == lane.id);

				//            /* Check dependencies */
				//            if (dependencies_in_lane.Count () > 0) {
				//                DBRevision revision = revisions.FirstOrDefault (r => r.id == revisionwork.revision_id);
				//                if (revision == null) {
				//                    revision = DBRevision_Extensions.Create (db, revisionwork.revision_id);
				//                    revisions.Add (revision);
				//                }

				//                Logger.Log (2, "CheckDependencies: Lane '{0}', revision '{1}' checking dependencies...", lane.lane, revision.revision);

				//                foreach (DBLaneDependency dependency in dependencies)
				//                    dependencies_satisfied &= dependency.IsSuccess (db, revision.revision);

				//                Logger.Log (2, "CheckDependencies: Lane '{0}', revision '{1}' dependency checking resulted in: {2}.", lane.lane, revision.revision, dependencies_satisfied);
				//            }

				//            if (!dependencies_satisfied)
				//                continue;

				//            Logger.Log (2, "CheckDependencies: Lane '{0}', revisionwork_id '{1}' dependencies fulfilled", lane.lane, revisionwork.id);

				//            sql.Length = 0;
				//            sql.AppendFormat ("UPDATE Work SET state = 0 WHERE revisionwork_id = {0};\n", revisionwork.id);
				//            sql.AppendFormat ("UPDATE RevisionWork SET state = 0 WHERE id = {0};\n", revisionwork.id);

				//            db.ExecuteNonQuery (sql.ToString ());
				//        }
				//    }
				//}
			} catch (Exception ex) {
				Logger.Log ("CheckDependencies: There was an exception while checking dependencies db: {0}", ex.ToString ());
			} finally {
				Logger.Log ("CheckDependencies: [Done in {0} seconds]", (DateTime.Now - start).TotalSeconds);
			}
		}

		public static void ReportCompletedRevisionWork (DB db, DBRevisionWork revision_work)
		{
			if (revision_work.State != DBState.Issues && revision_work.State != DBState.Success)
				return; // no condition was satisifed.

			var watch = new Stopwatch ();
			watch.Start ();
			var logName = "lane-" + revision_work.lane_id + ".log";

			try {
				var dependencies = DBLaneDependency_Extensions.GetDependencies (db, revision_work.lane_id);

				Logger.LogTo (1, logName, "ReportCompletedRevisionWork: Checking {0} dependencies", dependencies == null ? 0 : dependencies.Count);

				if (dependencies == null || dependencies.Count == 0)
					return;

				/* Check that there is only 1 dependency per lane and only DependentLaneSuccess condition */
				foreach (DBLaneDependency dep in dependencies) {
					if (dependencies.Any (dd => dep.id != dd.id && dep.lane_id == dd.lane_id)) {
						Logger.LogTo (0, logName, "ReportCompletedRevisionWork: lane {0} has multiple dependencies (currently not implemented)", dep.lane_id);
						return;
					}
					if (dep.Condition != DBLaneDependencyCondition.DependentLaneSuccess && dep.Condition != DBLaneDependencyCondition.DependentLaneIssuesOrSuccess) {
						Logger.LogTo (0, logName, "ReportCompletedRevisionWork: dep {0} has unsupported condition {1}", dep.id, dep.Condition);
						return;
					}
				}

				foreach (DBLaneDependency dependency in dependencies) {
					Logger.LogTo (1, logName, "ReportCompletedRevisionWork: Checking dependency {0} for lane {1}", dependency.id, dependency.lane_id);
					switch (dependency.Condition) {
					case DBLaneDependencyCondition.DependentLaneIssuesOrSuccess:
						break; // satisfied
					case DBLaneDependencyCondition.DependentLaneSuccess:
						if (revision_work.State != DBState.Success) {
							Logger.LogTo (2, logName, "ReportCompletedRevisionWork: dependency {0} not satisfied (revisionwork didn't succeed)", dependency.id);
							continue; // not satisifed
						}
						break;
					default:
						Logger.LogTo (2, logName, "ReportCompletedRevisionWork: dependency {0} not satisfied (unknown condition)", dependency.id);
						continue; // not satisfied (this needs extra handling)
					}

					if (dependency.dependent_host_id != null && revision_work.host_id != dependency.dependent_host_id.Value) {
						Logger.LogTo (2, logName, "ReportCompletedRevisionWork: dependency {0} not satisfied (wrong host)", dependency.id);
						continue; // wrong host
					}

					/* Find the revision works which has filfilled dependencies */
					using (IDbCommand cmd = db.CreateCommand ()) {
						cmd.CommandText = @"
SELECT RevisionWork.id
FROM RevisionWork
INNER JOIN Revision ON Revision.id = RevisionWork.revision_id
WHERE
	RevisionWork.lane_id = @lane_id AND
	Revision.revision = (SELECT revision FROM Revision WHERE id = @revision_id) AND
	RevisionWork.state = 9
INTO TEMPORARY UNLOGGED TABLE tmp;

UPDATE Work SET state = 0 WHERE revisionwork_id IN (SELECT * FROM tmp) AND state = 9;
UPDATE RevisionWork SET state = 0 WHERE id = IN (SELECT * FROM tmp) AND state = 9;
";
						DB.CreateParameter (cmd, "lane_id", dependency.lane_id);
						DB.CreateParameter (cmd, "revision_id", revision_work.revision_id);

						var rv = cmd.ExecuteNonQuery ();
						Logger.LogTo (1, logName, "ReportCompletedRevisionWork: dependency {0} resulted in {1} modified records", dependency.id, rv);
					}
				}
			} catch (Exception ex) {
				Logger.LogTo (logName, "ReportCompletedRevisionWork: There was an exception while checking dependencies db: {0}", ex.ToString ());
			} finally {
				Logger.LogTo (logName, "ReportCompletedRevisionWork: [Done in {0} seconds]", watch.Elapsed.TotalSeconds);
			}
		}

		private static void CheckDependencies (DB db, List<DBHost> hosts, List<DBLane> lanes, List<DBHostLane> hostlanes, ILogger log)
		{
			DateTime start = DateTime.Now;
			StringBuilder sql = new StringBuilder ();
			List<DBLaneDependency> dependencies;

			try {
				dependencies = DBLaneDependency_Extensions.GetDependencies (db, null);

				log.Log ("CheckDependencies: Checking {0} dependencies", dependencies == null ? 0 : dependencies.Count);

				if (dependencies == null || dependencies.Count == 0)
					return;

				/* Check that there is only 1 dependency per lane and only DependentLaneSuccess condition */
				foreach (DBLaneDependency dep in dependencies) {
					if (dependencies.Any (dd => dep.id != dd.id && dep.lane_id == dd.lane_id)) {
						CheckDependenciesSlow (db, hosts, lanes, hostlanes, dependencies);
						return;
					}
					if (dep.Condition != DBLaneDependencyCondition.DependentLaneSuccess && dep.Condition != DBLaneDependencyCondition.DependentLaneIssuesOrSuccess) {
						CheckDependenciesSlow (db, hosts, lanes, hostlanes, dependencies);
						return;
					}
				}

				foreach (DBLaneDependency dependency in dependencies) {
					log.Log ("CheckDependencies: Checking dependency {0} for lane {1}", dependency.id, dependency.lane_id);
					/* Find the revision works which has filfilled dependencies */
					using (IDbCommand cmd = db.CreateCommand ()) {
						cmd.CommandText = @"
SELECT RevisionWork.id
FROM RevisionWork
INNER JOIN Lane ON Lane.id = RevisionWork.lane_id
INNER JOIN Host ON Host.id = RevisionWork.host_id
INNER JOIN Revision ON Revision.id = RevisionWork.revision_id
INNER JOIN LaneDependency ON LaneDependency.lane_id = RevisionWork.lane_id
WHERE
	RevisionWork.lane_id = @lane_id AND RevisionWork.state = 9 AND
	EXISTS (
		SELECT SubRevisionWork.id
		FROM RevisionWork SubRevisionWork
		INNER JOIN Revision SubRevision ON SubRevisionWork.revision_id = SubRevision.id
		WHERE SubRevisionWork.completed = true AND ";
			if (dependency.Condition == DBLaneDependencyCondition.DependentLaneSuccess) {
				cmd.CommandText += "SubRevisionWork.state = 3 ";
			} else if (dependency.Condition == DBLaneDependencyCondition.DependentLaneIssuesOrSuccess) {
				cmd.CommandText += "(SubRevisionWork.state = 3 OR SubRevisionWork.state = 8) ";
			}
			
			cmd.CommandText +=
			@"AND SubRevision.revision = Revision.revision 
			AND SubRevisionWork.lane_id = @dependent_lane_id";

						if (dependency.dependent_host_id.HasValue) {
							DB.CreateParameter (cmd, "dependent_host_id", dependency.dependent_host_id.Value);
							cmd.CommandText += @"
			AND SubRevisionWork.host_id = @dependent_host_id";
						}

						cmd.CommandText += @"
		);";
						DB.CreateParameter (cmd, "lane_id", dependency.lane_id);
						DB.CreateParameter (cmd, "dependent_lane_id", dependency.dependent_lane_id);

						sql.Length = 0;
						using (IDataReader reader = cmd.ExecuteReader ()) {
							while (reader.Read ()) {
								int rw_id = reader.GetInt32 (0);
								sql.AppendFormat ("UPDATE Work SET state = 0 WHERE revisionwork_id = {0} AND state = 9;\n", rw_id);
								sql.AppendFormat ("UPDATE RevisionWork SET state = 0 WHERE id = {0} AND state = 9;\n", rw_id);
							}
						}
						db.ExecuteNonQuery (sql.ToString ());
					}


				}
			} catch (Exception ex) {
				log.Log ("CheckDependencies: There was an exception while checking dependencies db: {0}", ex.ToString ());
			} finally {
				log.Log ("CheckDependencies: [Done in {0} seconds]", (DateTime.Now - start).TotalSeconds);
			}
		}

		public static void FindPeopleForCommit (DBLane lane, DBRevision revision, List<DBPerson> people)
		{
			if (lane.source_control == "git") {
				GITUpdater.FindPeopleForCommit (lane, revision, people);
				/*
			} else if (lane.source_control == "svn") {
				SVNUpdater.FindPeopleForCommit (lane, revision, people);
				 * */
			} else {
				Logger.Log ("FindPeopleForCommit (): unknown source control: '{0}'", lane.source_control);
			}
		}
	}
}

