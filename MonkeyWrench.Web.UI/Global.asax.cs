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
using System.Web;

using MonkeyWrench.WebServices;

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
			Response.Clear ();
			Exception ex = Server.GetLastError ();
			Server.ClearError ();

			// Unwrap HttpUnhandledException
			Exception realException = ex.GetBaseException ();

			if (realException is HttpException && (realException as HttpException).GetHttpCode() == 404) {
				// Page not found.
				ErrorPage.transferToError (Server, Context, "Page not found.", HttpUtility.HtmlEncode (realException.Message), 404);
			} else if (realException is UnauthorizedException) {
				// User is not authorized to view this page, redirect to the login page.
				Response.Redirect (Configuration.WebSiteUrl + "/Login.aspx?referrer=" + HttpUtility.UrlEncode (Request.Url.ToString ()));
			} else {
				// Unhandled error. Log it and display an error page. 
				Logger.Log ("{0}: {1}", Request.Url.AbsoluteUri, ex);
				if (Request.IsLocal) {
					Response.StatusCode = 500;
					Response.Write ("<pre>");
					Response.Write (HttpUtility.HtmlEncode (ex.ToString ()));
					Response.Write ("</pre>");
				} else {
					ErrorPage.transferToError (Server, Context, "Internal Server Error",
						HttpUtility.HtmlEncode (realException.GetType ().Name) + ": " + HttpUtility.HtmlEncode (realException.Message),
						500);
				}
			}
		}

		protected void Session_End (object sender, EventArgs e)
		{

		}

		protected void Application_End (object sender, EventArgs e)
		{

		}
	}
}