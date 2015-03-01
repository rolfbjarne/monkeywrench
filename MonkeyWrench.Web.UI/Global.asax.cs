﻿/*
 * Global.asax.cs
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
using System.Linq;
using System.Web;
using System.Web.Security;
using System.Web.SessionState;

namespace MonkeyWrench.Web.UI
{
	public class Global : System.Web.HttpApplication
	{

		protected void Application_Start (object sender, EventArgs e)
		{
			Configuration.LoadConfiguration (new string [] {});
		}

		protected void Session_Start (object sender, EventArgs e)
		{

		}

		protected void Application_BeginRequest (object sender, EventArgs e)
		{

		}

		protected void Application_AuthenticateRequest (object sender, EventArgs e)
		{
		}

		protected void Application_Error (object sender, EventArgs e)
		{
			Exception ex = Server.GetLastError ();
			Logger.Log ("{0}: {1}", Request.Url.AbsoluteUri, ex);

			Response.StatusCode = 500;

			if (Request.IsLocal) {
				Response.Write ("<pre>");
				Response.Write (HttpUtility.HtmlEncode (ex.ToString ()));
				Response.Write ("</pre>");
			} else {
				var httpex = ex as HttpUnhandledException;
				if (httpex != null)
					ex = httpex.InnerException;

				Response.Write (String.Format (@"
					<!DOCTYPE html>
					<html>
					<head>
						<title>500 - Internal Server Error</title>
					</head>
					<body>
					<h1>Wrench encountered an error.</h1>
					<p>We're sorry about that. The error has been logged, and will hopefully be fixed soon!</p>
					<p>Error summary: <samp>{0}: {1}</samp></p>
					</body>
					</html>
				", HttpUtility.HtmlEncode (ex.GetType().Name), HttpUtility.HtmlEncode (ex.Message)));
			}
			Server.ClearError ();
		}

		protected void Session_End (object sender, EventArgs e)
		{

		}

		protected void Application_End (object sender, EventArgs e)
		{

		}
	}
}