﻿/*
 * DBLaneDependency_Extensions.cs
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
using System.Linq;
using System.Text;

using MonkeyWrench;
using MonkeyWrench.DataClasses;

namespace MonkeyWrench.Database
{
	public static class DBLaneDependency_Extensions
	{
		public static DBLaneDependency Create (DB db, int id)
		{
			return DBRecord_Extensions.Create (db, new DBLaneDependency (), DBLaneDependency.TableName, id);
		}

		/// <summary>
		/// Returns a list of all the dependencies for the specified lane.
		/// Returns null if there are no dependencies for the lane.
		/// </summary>
		/// <param name="lane"></param>
		/// <returns></returns>
		public static List<DBLaneDependency> GetDependencies (DB db, DBLane lane)
		{
			List<DBLaneDependency> result = null;

			using (IDbCommand cmd = db.CreateCommand ()) {
				cmd.CommandText = "SELECT * FROM LaneDependency WHERE lane_id = @lane_id;";
				DB.CreateParameter (cmd, "lane_id", lane.id);
				using (IDataReader reader = cmd.ExecuteReader ()) {
					while (reader.Read ()) {
						if (result == null)
							result = new List<DBLaneDependency> ();
						result.Add (new DBLaneDependency (reader));
					}
				}
			}

			return result;
		}

		public static bool IsSuccess (this DBLaneDependency me, DB db, string revision)
		{
			using (IDbCommand cmd = db.CreateCommand ()) {
				cmd.CommandText = @"
SELECT RevisionWork.id
FROM RevisionWork 
INNER JOIN Revision ON Revision.id = RevisionWork.revision_id
WHERE RevisionWork.lane_id = @lane_id AND RevisionWork.state = @success AND Revision.revision = @revision
";

				if (me.dependent_host_id.HasValue) {
					cmd.CommandText += " AND RevisionWork.host_id = @host_id";
					DB.CreateParameter (cmd, "host_id", me.dependent_host_id.Value);
				}

				switch (me.Condition) {
				case DBLaneDependencyCondition.DependentLaneSuccess:
					break;
				case DBLaneDependencyCondition.DependentLaneSuccessWithFile:
					cmd.CommandText += " AND WorkFile.filename = @filename";
					DB.CreateParameter (cmd, "filename", me.filename);
					break;
				default:
					Logger.Log ("LaneDependency '{0}' contains an unknown dependency condition: {1}", me.id, me.Condition);
					return false;
				}

				cmd.CommandText += " LIMIT 1;";

				DB.CreateParameter (cmd, "lane_id", me.dependent_lane_id);
				DB.CreateParameter (cmd, "revision", revision); // Don't join with id here, if the revision comes from another lane, it might have a different id
				DB.CreateParameter (cmd, "success", (int) DBState.Success);

				object obj = cmd.ExecuteScalar ();
				bool result = obj != null && !(obj is DBNull);

				Logger.Log ("Dependency id {0}: {1} (condition: {2}, revision: {3}, host_id: {4}, filename: {5}, lane: {6})", me.id, result, me.Condition, revision, me.dependent_host_id.HasValue ? me.dependent_host_id.Value.ToString () : "null", me.filename, me.dependent_lane_id);

				return result;
			}
		}
	}
}
