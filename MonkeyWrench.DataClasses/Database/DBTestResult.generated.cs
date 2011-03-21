/*
 * DBTestResult.generated.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (RKvinge@novell.com)
 *   
 * Copyright 2011 Novell, Inc. (http://www.novell.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */


/*
 * This file has been generated. 
 * If you modify it you'll loose your changes.
 */ 


using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Reflection;

#pragma warning disable 649

namespace MonkeyWrench.DataClasses
{
	public partial class DBTestResult : DBView
	{
		private string _md5;
		private string _filename;
		private int _command_id;
		private string _revision;
		private int _lane_id;

		public string @md5 { get { return _md5; } set { _md5 = value; } }
		public string @filename { get { return _filename; } set { _filename = value; } }
		public int @command_id { get { return _command_id; } set { _command_id = value; } }
		public string @revision { get { return _revision; } set { _revision = value; } }
		public int @lane_id { get { return _lane_id; } set { _lane_id = value; } }


		public const string SQL = 
@"SELECT  
		file.md5, workfile.id, workfile.filename, work.command_id, revision.revision, revisionwork.lane_id, revisionwork.host_id;";


		private static string [] _fields_ = new string [] { "md5", "filename", "command_id", "revision", "lane_id" };
		public override string [] Fields
		{
			get
			{
				return _fields_;
			}
		}
        

		public DBTestResult ()
		{
		}
	
		public DBTestResult (IDataReader reader) 
			: base (reader)
		{
		}


	}
}

