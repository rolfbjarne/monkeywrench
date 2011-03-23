/*
 * Program.cs
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
using System.Data.Common;
using System.IO;
using System.Text;

using MonkeyWrench;
using MonkeyWrench.Database;
using MonkeyWrench.DataClasses;

namespace MonkeyWrench.CleanFiles
{
	class Program
	{
		static void Log (string msg, params object [] args)
		{
			Console.WriteLine ("{0} {1}", DateTime.Now.ToLongTimeString (), string.Format (msg, args));
		}

		static void Main (string [] args)
		{
			int counter = 0;
			int existing_files = 0;
			int missing_files = 0;
			int chunk_missing_files = 0;
			int chunk_size = 250;
			int sleep_time = 15000;
			string fn;
			bool exists;
			StringBuilder delete_sql = new StringBuilder ();

			try {
				// be nice
				System.Diagnostics.Process.GetCurrentProcess ().PriorityClass = System.Diagnostics.ProcessPriorityClass.Idle;
				
				Configuration.LoadConfiguration (args);
				using (DB db = new DB ()) {
					using (DB write_db = new DB ()) {
						using (IDbCommand cmd = db.CreateCommand ()) {
							cmd.CommandText = "SELECT * FROM File;";
							using (IDataReader reader = cmd.ExecuteReader ()) {
								while (reader.Read ()) {
									DBFile f = new DBFile (reader);
									if (Configuration.LogVerbosity > 2)
										Log ("Read file #{2}: {0} {1}", f.md5, f.write_stamp, counter);
									exists = true;
									fn = FileUtilities.CreateFilename (f.md5, f.compressed_mime != null, false);
									if (!File.Exists (fn)) {
										fn = FileUtilities.CreateFilename (f.md5, f.compressed_mime == null, false);
										if (!File.Exists (fn)) {
											exists = false;
										}
									}
									if (exists) {
										existing_files++;
									} else {
										missing_files++;
										chunk_missing_files++;
										delete_sql.AppendFormat (@"
UPDATE Revision SET log_file_id = NULL WHERE log_file_id = {0};
UPDATE Revision SET diff_file_id = NULL WHERE diff_file_id = {0};
DELETE FROM WorkFile WHERE file_id = {0};
DELETE FROM File WHERE id = {0};
", f.id);
									}
									counter++;
									if (counter % chunk_size == 0) {
										if (chunk_missing_files > 0) {
											int result;
											Log ("Deleting {0} file records...", chunk_missing_files);
											using (IDbCommand dcmd = write_db.CreateCommand ()) {
												dcmd.CommandText = delete_sql.ToString ();
												result = dcmd.ExecuteNonQuery ();
											}
											Log ("Deleting {0} file records resulted in {1} affected rows.", chunk_missing_files, result);
										}
										Log ("Processed {0} files, sleeping a bit. So far {1} existing files and {2} missing files ({3} in this chunk).", counter, existing_files, missing_files, chunk_missing_files);
										System.Threading.Thread.Sleep (sleep_time);

										chunk_missing_files = 0;
										delete_sql.Length = 0;
									}
								}
							}
						}
					}
				}
			} catch (Exception ex) {
				Console.WriteLine (ex);
			}
		}
	}
}
