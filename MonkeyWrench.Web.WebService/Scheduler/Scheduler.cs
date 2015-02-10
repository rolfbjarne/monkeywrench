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
using System.Linq;
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
		static bool queued_forced = false;
		static HashSet<string> queued_repositories = new HashSet<string> ();
		static object queue_lock = new object ();

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

		public static void ExecuteSchedulerAsync (string[] repositories)
		{
			Async.Execute ((v) => {
				ExecuteScheduler (null, false, repositories, null);
			});
		}

		public static void ExecuteSchedulerSync (ILogger logger)
		{
			ExecuteScheduler (logger, false, null, null, true);
		}

		public static void ExecuteSchedulerSync (int lane_id, ILogger logger)
		{
			ExecuteScheduler (logger, false, null, new int [] { lane_id }, false);
		}

		public static bool ExecuteScheduler (bool forcefullupdate)
		{
			return ExecuteScheduler (null, forcefullupdate, null, null);
		}

		public static bool ExecuteScheduler (ILogger extra_log, bool forcefullupdate, string[] repos, int[] filter_to_lanes, bool queue_if_busy = true)
		{
			Stopwatch watch = new Stopwatch ();
			Lock scheduler_lock = null;
			List<DBLane> lanes;

			List<DBHost> hosts;
			List<DBHostLane> hostlanes;
			const string logName = "scheduler.log";

			ILogger log = new NamedLogger (logName);
			if (extra_log != null) {
				log = new AggregatedLogger (new ILogger [] { extra_log, log });
			}

			try {
				scheduler_lock = Lock.Create ("MonkeyWrench.Scheduler");
				if (scheduler_lock == null) {
					log.Log ("Could not aquire scheduler lock.");
					if (queue_if_busy) {
						lock (queued_repositories) {
							if (repos != null && repos.Length > 0) {
								foreach (var r in repos)
									queued_repositories.Add (r);
								log.Log ("Queued {0} repositories for later scheduling: {1}", repos.Length, string.Join (", ", repos));
							}
							queued_forced |= forcefullupdate;
						}
					}
					return false;
				}

				log.Log ("Scheduler lock aquired successfully.");
				
				is_executing = true;
				watch.Start ();

				using (DB db = new DB (true)) {
					lanes = db.GetAllLanes ();
					hosts = db.GetHosts ();
					hostlanes = db.GetAllHostLanes ();

					var filtered_lanes = SchedulerBase.FilterLanes (lanes, hostlanes, logName);

					if (filter_to_lanes != null && filter_to_lanes.Length > 0) {
						filtered_lanes.RemoveAll ((v) => !filter_to_lanes.Contains (v.id));
					}

					log.Log ("Scheduler will process {0} lanes", filtered_lanes.Count);

					// Collect the repositories to fetch
					var repositories = new HashSet<string> ();
					var lanes_for_repository = new Dictionary<string, List<DBLane>> ();
					foreach (var lane in filtered_lanes) {
						foreach (var repo in lane.repository.Split (new char [] { ',' }, StringSplitOptions.RemoveEmptyEntries)) {
							repositories.Add (repo);
							List<DBLane> l;
							if (!lanes_for_repository.TryGetValue (repo, out l)) {
								l = new List<DBLane> ();
								lanes_for_repository [repo] = l;
							}
							l.Add (lane);
						}
					}
					if (!forcefullupdate && repos != null && repos.Length > 0) {
						var input_repositories = new HashSet<string> ();
						foreach (var repo in repos)
							input_repositories.Add (repo);
						repositories.IntersectWith (input_repositories); // only process those that are both in filtered lanes, and in the input list.
					}
					// Fetch all the repositores we care about.
					var updater = new GITUpdater (repositories, forcefullupdate);
					var logs = updater.FetchGitRepositories (log);
					foreach (var kvp in logs) {
						foreach (DBLane lane in lanes_for_repository [kvp.Key]) {
							log.LogToRaw (Configuration.GetLogFileForLane (lane.id), kvp.Value.ToString ());
						}
					}
					log.Log ("Fetching revisions in {0} lanes", filtered_lanes.Count);
					foreach (DBLane lane in filtered_lanes)
						updater.UpdateRevisionsInDB (db, lane, hosts, hostlanes, log);

					AddRevisionWork (db, log);
					AddWork (db, hosts, filtered_lanes, lanes, hostlanes, log);
					if (forcefullupdate)
						CheckDependencies (db, hosts, lanes, hostlanes, log);
				}

				log.Log ("Scheduler finished successfully in {0} seconds.", watch.Elapsed.TotalSeconds);

				return true;
			} catch (Exception ex) {
				log.Log ("An exception occurred in the scheduler: {0}", ex.ToString ());
				return false;
			} finally {
				if (scheduler_lock != null)
					scheduler_lock.Unlock ();
				if (queue_if_busy) {
					HashSet<string> t_repositories;
					bool t_full;
					lock (queue_lock) {
						t_repositories = queued_repositories;
						t_full = queued_forced;
						queued_repositories = new HashSet<string> ();
						queued_forced = false;
					}
					is_executing = false;
					if (t_full || t_repositories.Count > 0) {
						Async.Execute ((v) => {
							ExecuteScheduler (extra_log, t_full, t_repositories.ToArray (), filter_to_lanes, queue_if_busy);
						});
					}
				}
			}
		}

		/// <summary>
		/// Returns true if something was added to the database.
		/// </summary>
		/// <param name="db"></param>
		/// <param name="lane"></param>
		/// <param name="host"></param>
		/// <returns></returns>
		public static void AddRevisionWork (DB db, ILogger log)
		{
			var watch = new Stopwatch ();
			watch.Start ();

			try {
				using (IDbCommand cmd = db.CreateCommand ()) {
					cmd.CommandText = @"
INSERT INTO RevisionWork (lane_id, revision_id, host_id, state) 
SELECT Lane.id AS lid, Revision.id AS rid, Host.id AS hid, 10 as state
FROM HostLane
INNER JOIN Host ON HostLane.host_id = Host.id
INNER JOIN Lane ON HostLane.lane_id = Lane.id
INNER JOIN Revision ON Revision.lane_id = lane.id
WHERE HostLane.enabled = true AND
	NOT EXISTS (
		SELECT 1
		FROM RevisionWork 
		WHERE RevisionWork.lane_id = Lane.id AND RevisionWork.host_id = Host.id AND RevisionWork.revision_id = Revision.id
		);
";
					try {
						var rv = cmd.ExecuteNonQuery ();
						log.Log ("AddRevisionWork: Added {0} records.", rv);
					} catch (Exception ex) {
						log.Log ("Failed to add revisionwork: {0}\n{1}", ex.Message, cmd.CommandText);
						throw;
					}
				}
			} catch (Exception ex) {
				log.Log ("AddRevisionWork got an exception: {0}\n{1}", ex.Message, ex.StackTrace);
			} finally {
				log.Log ("AddRevisionWork done in {0} seconds", watch.Elapsed.TotalSeconds);
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
