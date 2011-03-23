﻿/*
 * DBWork_Extensions.cs
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
using System.IO;
using System.Linq;
using System.Text;

using MonkeyWrench.DataClasses;
using MonkeyWrench.Database;

namespace MonkeyWrench.Database
{
	public static class DBWork_Extensions
	{
		public static DBWork Create (DB db, int id)
		{
			return DBRecord_Extensions.Create (db, new DBWork (), DBWork.TableName, id);
		}


		public static void UpdateRevisionWorkState (this DBWork w, DB db)
		{
			DBRevisionWork rw = w.GetRevisionWork (db);

			if (rw != null)
				rw.UpdateState (db);
		}
		
		public static void Pause (DB db, int id)
		{
			DBWork work = DBWork_Extensions.Create (db, id);
			if (work.State == DBState.NotDone) {
				work.State = DBState.Paused;
				work.Save (db);
				work.UpdateRevisionWorkState (db);
			}
		}

		public static void Resume (DB db, int id)
		{
			DBWork work = DBWork_Extensions.Create (db, id);
			if (work.State == DBState.Paused) {
				work.State = DBState.NotDone;
				work.Save (db);
				work.UpdateRevisionWorkState (db);
			}
		}

		public static void Abort (DB db, int id)
		{
			DBWork work = DBWork_Extensions.Create (db, id);
			work.State = DBState.Aborted;
			work.Save (db);
			work.UpdateRevisionWorkState (db);
		}

		public static void Clear (DB db, int id)
		{
			DBWork work = DBWork_Extensions.Create (db, id);
			work.State = DBState.NotDone;
			work.summary = string.Empty;
			work.Save (db);
			work.UpdateRevisionWorkState (db);
		}

		public static void AddFile (this DBWork me, DB db, DBFile file, string filename, bool hidden)
		{
			using (IDbCommand cmd = db.CreateCommand ()) {
				cmd.CommandText = "INSERT INTO WorkFile (work_id, file_id, hidden, filename) VALUES (@work_id, @file_id, @hidden, @filename);";
				DB.CreateParameter (cmd, "work_id", me.id);
				DB.CreateParameter (cmd, "file_id", file.id);
				DB.CreateParameter (cmd, "hidden", hidden);
				DB.CreateParameter (cmd, "filename", filename);
				cmd.ExecuteNonQuery ();
			}
		}

		public static void RemoveFile (this DBWork me, DB db, DBFile file)
		{
			using (IDbCommand cmd = db.CreateCommand ()) {
				cmd.CommandText = "DELETE FROM WorkFile WHERE work_id = @work_id AND file_id = @file_id;";
				DB.CreateParameter (cmd, "work_id", me.id);
				DB.CreateParameter (cmd, "file_id", file.id);
				cmd.ExecuteNonQuery ();
			}
		}

		public static void Delete (DB db, int id, string Table)
		{
			if (id <= 0)
				throw new Exception ("Invalid id.");

			using (IDbCommand cmd = db.CreateCommand ()) {
				cmd.CommandText =
					"DELETE FROM WorkFile WHERE work_id = @id; " +
					"DELETE FROM Work WHERE id = @id; ";
				DB.CreateParameter (cmd, "id", id);
				cmd.ExecuteNonQuery ();
			}
		}

		public static DBRevisionWork GetRevisionWork (this DBWork me, DB db)
		{
			return DBRevisionWork_Extensions.Create (db, me.revisionwork_id);
		}

		public static DBFile GetFile (DB db, int work_id, string filename, bool throw_on_multiple)
		{
			DBFile result = null;

			using (IDbCommand cmd = db.CreateCommand ()) {
				cmd.CommandText =
@"
SELECT File.id, File.md5, File.file_id, File.mime, File.compressed_mime, File.size, File.file_id, File.write_stamp, File.hidden OR WorkFile.hidden AS hidden,
	CASE WHEN WorkFile.filename = '' THEN File.filename ELSE WorkFile.filename END
	FROM WorkFile
		INNER JOIN File ON WorkFile.file_id = File.id
    WHERE WorkFile.work_id = @work_id AND (WorkFile.filename = @filename OR (WorkFile.filename = '' AND File.filename = @filename));
";
				DB.CreateParameter (cmd, "work_id", work_id);
				DB.CreateParameter (cmd, "filename", filename);

				using (IDataReader reader = cmd.ExecuteReader ()) {
					if (!reader.Read ())
						return null;

					result = new DBFile (reader);

					if (throw_on_multiple && reader.Read ())
						throw new Exception (string.Format ("Found more than one file in work with id {0} whose filename is '{1}'", work_id, filename));
				}
			}


			return result;
		}

		/// <summary>
		/// Returns a READ-ONLY list of files
		/// </summary>
		/// <param name="db"></param>
		/// <param name="work_id"></param>
		/// <param name="include_hidden_files">If hidden files are included in the returned list. Note that any file named 'index.html' is always returned, even if it's hidden and this parameter is false.</param>
		/// <returns></returns>
		public static List<DBWorkFileView> GetFiles (DB db, int work_id, bool include_hidden_files)
		{
			List<DBWorkFileView> result = new List<DBWorkFileView> ();

			using (IDbCommand cmd = db.CreateCommand ()) {
				if (include_hidden_files) {
					cmd.CommandText = "SELECT * FROM WorkFileView WHERE work_id = @work_id;";
				} else {
					cmd.CommandText = "SELECT * FROM WorkFileView WHERE work_id = @work_id AND (hidden = false OR filename = 'index.html');";
				}
				DB.CreateParameter (cmd, "work_id", work_id);
				using (IDataReader reader = cmd.ExecuteReader ()) {
					while (reader.Read ()) {
						DBWorkFileView file = new DBWorkFileView (reader);
						result.Add (file);
					}
				}
			}

			return result;
		}
		
		public static List<DBWorkFileView> GetFiles (DB db, int work_id)
		{
			return GetFiles (db, work_id, true);
		}

		public static List<DBWorkFileView> GetFiles (this DBWork me, DB db)
		{
			return GetFiles (db, me.id);
		}

		public static DBFile AddFile (this DBWork me, DB db, string path, string filename, bool hidden, string compressed_mime)
		{
			DBFile result = db.Upload (path, Path.GetExtension (filename), hidden, compressed_mime);
			me.AddFile (db, result, filename, hidden);
			return result;
		}

		public static void SetState (DB db, int id, DBState old_state, DBState new_state)
		{
			using (IDbCommand cmd = db.CreateCommand ()) {
				cmd.CommandText = "UPDATE Work SET state = @new_state WHERE state = @old_state AND id = @id;";
				DB.CreateParameter (cmd, "id", id);
				DB.CreateParameter (cmd, "old_state", (int) old_state);
				DB.CreateParameter (cmd, "new_state", (int) new_state);
				cmd.ExecuteNonQuery ();
			}
		}
	}
}
