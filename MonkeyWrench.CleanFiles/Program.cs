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
			try {
				Configuration.LoadConfiguration (args);
				using (DB db = new DB ()) {
					using (IDbCommand cmd = db.CreateCommand ()) {
						cmd.CommandText = "SELECT * FROM File;";
						using (IDataReader reader = cmd.ExecuteReader ()) {
							while (reader.Read ()) {
								DBFile f = new DBFile (reader);
								Console.WriteLine ("Read file #{2}: {0} {1}", f.md5, f.write_stamp, counter);
								counter++;
								if (counter % 100 == 0) {
									Console.WriteLine ("Sleeping a bit...");
									System.Threading.Thread.Sleep (15000);
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
