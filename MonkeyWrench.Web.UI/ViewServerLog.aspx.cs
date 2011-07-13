/*
 * ViewServerLog.aspx.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (rolf@xamarin.com)
 *   
 * Copyright 2011 Xamarin Inc. (http://www.xamarin.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

using MonkeyWrench.DataClasses;
using MonkeyWrench.DataClasses.Logic;
using MonkeyWrench.Web.WebServices;

public partial class ViewServerLog : System.Web.UI.Page
{
	private new Master Master
	{
		get { return base.Master as Master; }
	}

	protected override void OnInit (EventArgs e)
	{
		base.OnInit (e);

		try {
			LoginResponse response = Master.WebService.Login (Master.WebServiceLogin);

			if (!Authentication.IsInRole (response, Roles.Administrator)) {
				divLog.Text = "You need admin rights.";
				return;
			}

			using (FileStream fs = new FileStream (MonkeyWrench.Configuration.LogFile, FileMode.Open, FileAccess.Read)) {
				fs.Seek (fs.Length > 4096 ? fs.Length - 4096 : 0, SeekOrigin.Begin);
				using (StreamReader reader = new StreamReader (fs)) {
					divLog.Text = reader.ReadToEnd ().Replace ("\n", "<br/>").Replace ("\r", "").Replace (" ", "&nbsp;");
				}
			}
		} catch (Exception ex) {
			divLog.Text = Utils.FormatException (ex, true);
		}
	}
}

