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
		static void Main (string [] args)
		{
			int counter = 0;
			int existing_files = 0;
			int missing_files = 0;
			string fn;
			bool exists;

			try {
				Configuration.LoadConfiguration (args);
				using (DB db = new DB ()) {
					using (DB write_db = new DB ()) {
						using (IDbCommand cmd = db.CreateCommand ()) {
							cmd.CommandText = "SELECT * FROM File;";
							using (IDataReader reader = cmd.ExecuteReader ()) {
								while (reader.Read ()) {
									DBFile f = new DBFile (reader);
									Console.WriteLine ("Read file #{2}: {0} {1}", f.md5, f.write_stamp, counter);
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
									}
									counter++;
									if (counter % 1000 == 0) {
										Console.WriteLine ("Processed {0} files, sleeping a bit. So far {1} existing files and {2} missing files.", counter, existing_files, missing_files);
										System.Threading.Thread.Sleep (15000);
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
