/*
 * DBTryCommit_Extensions.cs
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
using System.Data;
using System.Linq;
using System.Text;

using MonkeyWrench.DataClasses;

namespace MonkeyWrench.Database
{
	public static class DBTryCommit_Extensions
	{
		public static void Delete (DB db, int id)
		{
			int result;

			using (IDbTransaction transaction = db.BeginTransaction ()) {
				using (IDbCommand cmd = db.CreateCommand ()) {
					cmd.Transaction = transaction;
					cmd.CommandText = @"
DELETE FROM RevisionWork WHERE id = (SELECT revisionwork_id FROM TryCommit WHERE id = @id);
DELETE FROM TryCommit WHERE id = @id;
";
					DB.CreateParameter (cmd, "id", id);
					result = cmd.ExecuteNonQuery ();
					Logger.Log ("Deleting try commit resulted in {0} affected records", result);
				}
				transaction.Commit ();
			}
		}
	}
}
