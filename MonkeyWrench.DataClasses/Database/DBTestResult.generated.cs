/*
 * DBTestResult.generated.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (RKvinge@novell.com)
 *   
 * Copyright 2009 Novell, Inc. (http://www.novell.com)
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
		private int _workfile_id;
		private string _md5;
		private string _filename;
		private int _command_id;
		private string _revision;
		private int _revisionwork_id;
		private int _lane_id;
		private int _host_id;

		public int @workfile_id { get { return _workfile_id; } set { _workfile_id = value; } }
		public string @md5 { get { return _md5; } set { _md5 = value; } }
		public string @filename { get { return _filename; } set { _filename = value; } }
		public int @command_id { get { return _command_id; } set { _command_id = value; } }
		public string @revision { get { return _revision; } set { _revision = value; } }
		public int @revisionwork_id { get { return _revisionwork_id; } set { _revisionwork_id = value; } }
		public int @lane_id { get { return _lane_id; } set { _lane_id = value; } }
		public int @host_id { get { return _host_id; } set { _host_id = value; } }


		public const string SQL = 
@"SELECT  
		0 as id, workfile.id as workfile_id, file.md5, workfile.filename, work.command_id, revision.revision, revisionwork.id as revisionwork_id, revisionwork.lane_id, revisionwork.host_id FROM Work;";


		private static string [] _fields_ = new string [] { "workfile_id", "md5", "filename", "command_id", "revision", "revisionwork_id", "lane_id", "host_id" };
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

