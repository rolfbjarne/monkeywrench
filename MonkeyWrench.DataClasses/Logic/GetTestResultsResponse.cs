﻿/*
 * GetTestResultsResponse.cs
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
using System.Linq;
using System.Text;

namespace MonkeyWrench.DataClasses.Logic
{
	public class GetTestResultsResponse : WebServiceResponse
	{
		public List<DBTestResult> Results;
	}
}
