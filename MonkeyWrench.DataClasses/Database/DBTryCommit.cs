/*
 * DBTryCommit.cs
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
using System.Text;
using System.Data;
using System.Data.Common;

#pragma warning disable 649

namespace MonkeyWrench.DataClasses
{
	public partial class DBTryCommit : DBRecord
	{
		public const string TableName = "TryCommit";

		public DBTryCommit ()
		{
		}

		public DBTryCommit (IDataReader reader) 
			: base (reader)
		{
		}

		public DBTryCommitAction SuccessfulAction
		{
			get { return (DBTryCommitAction) successful_action; }
			set { successful_action = (int) value; }
		}
	}
}

