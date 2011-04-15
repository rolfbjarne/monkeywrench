/*
 * DBTryCommit.generated.cs
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
	public partial class DBTryCommit : DBRecord
	{
		private int _revisionwork_id;
		private int _successful_action;
		private string _branch;

		public int @revisionwork_id { get { return _revisionwork_id; } set { _revisionwork_id = value; } }
		public int @successful_action { get { return _successful_action; } set { _successful_action = value; } }
		public string @branch { get { return _branch; } set { _branch = value; } }


		public override string Table
		{
			get { return "TryCommit"; }
		}
        

		public override string [] Fields
		{
			get
			{
				return new string [] { "revisionwork_id", "successful_action", "branch" };
			}
		}
        

	}
}

