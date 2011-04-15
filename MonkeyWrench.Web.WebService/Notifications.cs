﻿/*
 * WebServices.asmx.cs
 *
 * Authors:
 *   Rolf Bjarne Kvinge (RKvinge@novell.com)
 *   
 * Copyright 2010 Novell, Inc. (http://www.novell.com)
 *
 * See the LICENSE file included with the distribution for details.
 *
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Services;

using MonkeyWrench.Database;
using MonkeyWrench.DataClasses;
using MonkeyWrench.DataClasses.Logic;

using Meebey.SmartIrc4net;

namespace MonkeyWrench.Web.WebService
{
	public static class Notifications
	{
		static object lock_obj = new object ();
		static List<NotificationBase> notifications = new List<NotificationBase> ();
		static Dictionary<int, List<NotificationBase>> notifications_per_lane = new Dictionary<int, List<NotificationBase>> ();

		public static void Start ()
		{
			List<DBLaneNotification> lane_notifications = new List<DBLaneNotification> ();

			System.Net.ServicePointManager.ServerCertificateValidationCallback += HandleCertificateValidation;

			lock (lock_obj) {
				using (DB db = new DB ()) {
					using (IDbCommand cmd = db.CreateCommand ()) {
						/* Only select the notifications that are actually in use */
						cmd.CommandText = "SELECT Notification.* FROM Notification WHERE id IN (SELECT DISTINCT notification_id FROM LaneNotification);";
						cmd.CommandText += "SELECT * FROM LaneNotification;";
						using (IDataReader reader = cmd.ExecuteReader ()) {
							while (reader.Read ()) {
								DBNotification n = new DBNotification (reader);
								if (n.ircidentity_id.HasValue) {
									Logger.Log ("Starting irc notification");
									notifications.Add (new IrcNotification (n));
								} else if (n.emailidentity_id.HasValue) {
									Logger.Log ("Starting email notification");
									notifications.Add (new EMailNotification (n));
								} else {
									Logger.Log ("Starting unknown notification");
								}
							}
							if (reader.NextResult ()) {
								while (reader.Read ()) {
									lane_notifications.Add (new DBLaneNotification (reader));
								}
							}
						}
					}
				}

				foreach (DBLaneNotification ln in lane_notifications) {
					List<NotificationBase> ns;
					NotificationBase n;
					if (!notifications_per_lane.TryGetValue (ln.lane_id, out ns)) {
						ns = new List<NotificationBase> ();
						notifications_per_lane [ln.lane_id] = ns;
					}
					n = notifications.First ((v) => v.Notification.id == ln.notification_id);
					ns.Add (n);
					Logger.Log ("Notifications: enabled notification {0} '{1}' for lane {2}", n.Notification.id, n.Notification.name, ln.lane_id);
				}
			}
		}

		public static void Restart ()
		{
			Stop ();
			Start ();
		}

		public static void Stop ()
		{
			lock (lock_obj) {
				if (notifications != null) {
					foreach (var n in notifications) {
						n.Stop ();
					}
					notifications.Clear ();
				}
			}
		}

		public static void Notify (DBWork work, DBRevisionWork revision_work)
		{
			Logger.Log ("Notifications.Notify (lane_id: {1} revision_id: {2} host_id: {3} State: {0} Completed: {4})", work.State, revision_work.lane_id, revision_work.revision_id, revision_work.host_id, revision_work.completed);
			if (notifications == null)
				return;

			if (revision_work.completed) {
				using (DB db = new DB ()) {
					using (IDbCommand cmd = db.CreateCommand ()) {
						cmd.CommandText = "SELECT * FROM TryCommit WHERE revisionwork_id = " + revision_work.id + ";";
						using (IDataReader reader = cmd.ExecuteReader ()) {
							if (reader.Read ()) {
								DBTryCommit tc = new DBTryCommit (reader);
								ThreadPool.QueueUserWorkItem ((v) =>
								{
									try {
										ProcessTryCommit (revision_work, tc);
									} catch {
										// just swallow
									}
								});
							}
						}
					}
				}
			}

			if (!(work.State == DBState.Failed || work.State == DBState.Issues))
				return;

			ThreadPool.QueueUserWorkItem ((v) =>
				{
					try {
						ProcessNotify (work, revision_work);
					} catch {
						// just swallow
					}
				});
		}

		private static bool HandleCertificateValidation (object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors)
		{
			/* Accept any errors */
			return true;
		}

		private static void ExecuteTryCommitAction (DBRevisionWork revision_work, DBRevision revision, DBLane lane, DBTryCommit try_commit, StringBuilder log, out string summary)
		{
			if (revision_work.State != DBState.Success) {
				summary = "Action not executed since the commit failed.";
				return;
			}

			log.AppendFormat ("Action upon success: {0}", try_commit.SuccessfulAction.ToString ());
			log.AppendLine ();

			try {
				switch (try_commit.SuccessfulAction) {
				case DBTryCommitAction.None:
					summary = "Action upon success: None";
					return;
				case DBTryCommitAction.CherryPick:
					summary = "Action upon success: Cherry pick";
					break;
				case DBTryCommitAction.Merge:
					summary = "Action upon success: Merge";
					break;
				default:
					summary = "Action upon success: Unknown.";
					return;
				}

				if (MonkeyWrench.Scheduler.GITUpdater.ExecuteTryCommit (lane, try_commit, revision, log)) {
					summary += " (Succeeded).";
				} else {
					summary += " (Failed - see attached log for more information).";
				}
			} catch (Exception ex) {
				log.AppendFormat ("Failed: {0}", ex.Message);
				log.AppendLine ();
				summary = "Action upon success failed due to exception. See attached log for more information.";
			}
		}

		private static void ProcessTryCommit (DBRevisionWork revision_work, DBTryCommit try_commit)
		{
			try {
				Logger.Log ("Notifications.ProcessTryCommit (lane_id: {0} revision_id: {1} host_id: {2} State: {3})", revision_work.lane_id, revision_work.revision_id, revision_work.host_id, revision_work.State);
				DBLane lane;
				DBRevision revision;
				DBHost host;
				List<DBPerson> people = new List<DBPerson> ();
				string link;
				StringBuilder subject = new StringBuilder ();
				StringBuilder body = new StringBuilder ();
				StringBuilder body_html = new StringBuilder ();
				string action_summary;
				StringBuilder action_log = new StringBuilder ();
				System.Net.Mail.Attachment action_log_attach;
				System.Net.Mail.Attachment body_html_attach;

				using (DB db = new DB ()) {
					revision = DBRevision_Extensions.Create (db, revision_work.revision_id);
					lane = DBLane_Extensions.Create (db, revision_work.lane_id);
					host = DBHost_Extensions.Create (db, revision_work.host_id);
				}

				ExecuteTryCommitAction (revision_work, revision, lane, try_commit, action_log, out action_summary);
				action_log_attach = System.Net.Mail.Attachment.CreateAttachmentFromString (action_log.ToString (), "action.log", Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Plain);

				// Create mail

				subject.Append ("[MonkeyWrench] ");
				subject.Append (revision_work.State.ToString ());
				subject.Append (" ");
				subject.Append (revision.revision);
				subject.Append ("/");
				subject.Append (lane.lane);
				subject.Append ("/");
				subject.Append (host.host);

				body.AppendLine ("Execution result for:");
				body.AppendFormat ("    Revision: {0}\n", revision.revision);
				body.AppendFormat ("    Host: {0}\n", host.host);
				body.AppendFormat ("    Lane: {0}\n", lane.lane);
				link = string.Format ("{0}/ViewLane.aspx?lane_id={1}&host_id={2}&revision_id={3}", Configuration.WebSiteUrl, lane.id, host.id, revision.id);
				body.AppendLine (link);
				body.AppendLine (action_summary);

				using (System.Net.WebClient wc = new System.Net.WebClient ()) {
					body_html.Append (wc.DownloadString (link));
					body_html.Replace ("\"res/default.css\"", string.Format ("'{0}/res/default.css'", Configuration.WebSiteUrl));
					body_html.Replace ("a href=\"", string.Format ("a href=\"{0}/", Configuration.WebSiteUrl));
					body_html.Replace ("a href=\'", string.Format ("a href=\'{0}/", Configuration.WebSiteUrl));
					body_html.Replace (" src=\"", string.Format (" src=\"{0}/", Configuration.WebSiteUrl));
					body_html.Replace (" src=\'", string.Format (" src=\'{0}/", Configuration.WebSiteUrl));
					body_html_attach = System.Net.Mail.Attachment.CreateAttachmentFromString (body_html.ToString (), "body.html", Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Html);
					body_html.Length = 0;
				}

				MonkeyWrench.Scheduler.Scheduler.FindPeopleForCommit (lane, revision, people);
				SendMail (people, subject, body, body_html, action_log_attach, body_html_attach);

				Logger.Log ("Mail sent successfully");
			} catch (Exception ex) {
				Logger.Log ("Exception while processing try commits: {0}", ex.Message);
			}
		}

		private static void SendMail (List<DBPerson> people, StringBuilder subject, StringBuilder body, StringBuilder html_body, params System.Net.Mail.Attachment [] attachments)
		{
			using (var mail = new System.Net.Mail.MailMessage ()) {
				mail.From = new System.Net.Mail.MailAddress (Configuration.SMTPUser, "MonkeyWrench");
				foreach (DBPerson person in people) {
					foreach (string to in person.Emails) {
						mail.CC.Add (new System.Net.Mail.MailAddress (to, person.fullname));
					}
				}
				mail.CC.Add (new System.Net.Mail.MailAddress (Configuration.SMTPUser, "MonkeyWrench"));
				mail.Body = body.ToString ();
				mail.Subject = subject.ToString ();
				foreach (var attach in attachments) {
					if (attach == null)
						continue;
					mail.Attachments.Add (attach);
				}
				if (html_body.Length > 0)
					mail.AlternateViews.Add (System.Net.Mail.AlternateView.CreateAlternateViewFromString (html_body.ToString (), System.Text.Encoding.UTF8, System.Net.Mime.MediaTypeNames.Text.Html));
				SendMail (mail);
			}
		}

		private static void SendMail (System.Net.Mail.MailMessage mail)
		{
			string host = Configuration.SMTPHost;
			string port = "25";
			int c = host.IndexOf (':');
			if (c >= 0) {
				port = host.Substring (c + 1);
				host = host.Substring (0, c);
			}

			System.Net.Mail.SmtpClient client = new System.Net.Mail.SmtpClient (host, int.Parse (port));
			client.EnableSsl = true;
			client.Timeout = 10000;
			client.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
			client.UseDefaultCredentials = false;
			client.Credentials = new System.Net.NetworkCredential (Configuration.SMTPUser, Configuration.SMTPPassword);
			client.Send (mail);
		}

		private static void ProcessNotify (DBWork work, DBRevisionWork revision_work)
		{
			List<NotificationBase> notifications;

			Logger.Log ("Notifications.ProcessNotify (lane_id: {1} revision_id: {2} host_id: {3} State: {0})", work.State, revision_work.lane_id, revision_work.revision_id, revision_work.host_id);

			try {
				lock (lock_obj) {
					if (!notifications_per_lane.TryGetValue (revision_work.lane_id, out notifications)) {
						Logger.Log ("Notifications.ProcessNotify (lane_id: {1} revision_id: {2} host_id: {3} State: {0}): Lane doesn't have any notifications enabled", work.State, revision_work.lane_id, revision_work.revision_id, revision_work.host_id);
						return;
					}

					foreach (var notification in notifications) {
						notification.Notify (work, revision_work);
					}
				}
			} catch (Exception ex) {
				Logger.Log ("Exception while processing notification: {0}", ex.Message);
			}
		}
	}

	public abstract class NotificationBase
	{
		protected NotificationBase (DBNotification notification)
		{
			Notification = notification;
		}

		public DBNotification Notification { get; private set; }

		private bool Evaluate (DBWork work, DBRevisionWork revision_work)
		{
			DBState newest_state = work.State;

			/* We need to see if the latest completed build is still failing */
			using (DB db = new DB ()) {
				using (IDbCommand cmd = db.CreateCommand ()) {
					cmd.CommandText = @"
SELECT state FROM RevisionWork
INNER JOIN Revision ON RevisionWork.revision_id = Revision.id
WHERE RevisionWork.lane_id = @lane_id AND RevisionWork.host_id = @host_id AND RevisionWork.completed AND Revision.date > (SELECT date FROM Revision WHERE id = @revision_id)
ORDER BY Revision.date DESC
LIMIT 1
";
					DB.CreateParameter (cmd, "lane_id", revision_work.lane_id);
					DB.CreateParameter (cmd, "host_id", revision_work.host_id);
					DB.CreateParameter (cmd, "revision_id", revision_work.revision_id);
					object obj_state = cmd.ExecuteScalar ();

					if (obj_state is DBState)
						newest_state = (DBState) obj_state;
				}
			}

			if (newest_state == DBState.Success)
				return false;

			switch (Notification.Type) {
			case DBNotificationType.AllFailures:
				return work.State == DBState.Issues || work.State == DBState.Failed;
			case DBNotificationType.FatalFailuresOnly:
				return work.State == DBState.Failed && newest_state == DBState.Failed;
			case DBNotificationType.NonFatalFailuresOnly:
				return work.State == DBState.Issues;
			default:
				return false;
			}
		}

		public abstract void Stop ();
		protected abstract void Notify (DBWork work, DBRevisionWork revision_work, List<DBPerson> people, string message);
		public void Notify (DBWork work, DBRevisionWork revision_work)
		{
			List<DBPerson> people = new List<DBPerson> ();
			DBRevision revision;
			DBLane lane;
			DBHost host;
			string message;

			Logger.Log ("NotificationBase.Notify (lane_id: {1} revision_id: {2} host_id: {3} State: {0})", work.State, revision_work.lane_id, revision_work.revision_id, revision_work.host_id);

			if (!Evaluate (work, revision_work)) {
				Logger.Log ("NotificationBase.Notify (lane_id: {1} revision_id: {2} host_id: {3} State: {0}) = evaluation returned false", work.State, revision_work.lane_id, revision_work.revision_id, revision_work.host_id);
				return;
			}

			switch (work.State) {
			case DBState.Issues:
				message = "Test failure";
				break;
			case DBState.Failed:
				message = "{red}{bold}Build failure{default}";
				break;
			default:
				message = "Unknown failure";
				break;
			}

			using (DB db = new DB ()) {
				revision = DBRevision_Extensions.Create (db, revision_work.revision_id);
				lane = DBLane_Extensions.Create (db, revision_work.lane_id);
				host = DBHost_Extensions.Create (db, revision_work.host_id);
			}

			message = string.Format ("{0} in revision {1} on {2}/{3}: {4}/ViewLane.aspx?lane_id={5}&host_id={6}&revision_id={7}",
				message,
				(revision.revision.Length > 8 ? revision.revision.Substring (0, 8) : revision.revision),
				lane.lane, host.host, Configuration.GetWebSiteUrl (), lane.id, host.id, revision.id);

			MonkeyWrench.Scheduler.Scheduler.FindPeopleForCommit (lane, revision, people);
			people = FindPeople (people);

			Notify (work, revision_work, people, message);
		}

		private List<DBPerson> FindPeople (List<DBPerson> people)
		{
			List<DBPerson> result = new List<DBPerson> ();
			for (int i = 0; i < people.Count; i++) {
				FindPerson (people [i], result);
			}
			return result;
		}

		private void FindPerson (DBPerson person, List<DBPerson> people)
		{
			using (DB db = new DB ()) {
				using (IDbCommand cmd = db.CreateCommand ()) {
					cmd.CommandText = string.Empty;

					// find registered people with the same email
					if (person.Emails != null) {
						int email_counter = 0;
						foreach (string email in person.Emails) {
							if (string.IsNullOrEmpty (email))
								continue;
							email_counter++;
							cmd.CommandText += "SELECT Person.* FROM Person INNER JOIN UserEmail ON Person.id = UserEmail.person_id WHERE UserEmail.email ILIKE @email" + email_counter.ToString () + ";\n";
							DB.CreateParameter (cmd, "email" + email_counter.ToString (), email);
						}
					}

					// find registered people with the same fullname
					if (!string.IsNullOrEmpty (person.fullname)) {
						cmd.CommandText += "SELECT Person.* FROM Person WHERE fullname ILIKE @fullname;";
						DB.CreateParameter (cmd, "fullname", person.fullname);
					}

					using (IDataReader reader = cmd.ExecuteReader ()) {
						do {
							while (reader.Read ()) {
								DBPerson guy = new DBPerson (reader);
								if (people.Exists ((v) => v.id == guy.id))
									continue;
								people.Add (guy);
							}
						} while (reader.NextResult ());
					}
				}
			}

			if (people.Count == 0)
				people.Add (person);
		}
	}

	public class IrcNotification : NotificationBase
	{
		Thread thread;
		IrcClient irc;
		DBIrcIdentity identity;
		bool enabled = true;

		public IrcNotification (DBNotification notification)
			: base (notification)
		{
			/* Connect to server and join channels */

			using (DB db = new DB ()) {
				using (IDbCommand cmd = db.CreateCommand ()) {
					cmd.CommandText = "SELECT * FROM IrcIdentity WHERE id = @id;";
					DB.CreateParameter (cmd, "id", notification.ircidentity_id.Value);
					using (IDataReader reader = cmd.ExecuteReader ()) {
						if (!reader.Read ())
							throw new ApplicationException (string.Format ("Could not find the irc identity {0}", notification.ircidentity_id.Value));
						identity = new DBIrcIdentity (reader);
					}
				}
			}

			Connect ();
		}

		public override void Stop ()
		{
			Disconnect ();
		}

		private void Connect ()
		{
			irc = new IrcClient ();

			thread = new Thread (Loop);
			thread.Start ();
		}

		private void Disconnect ()
		{
			try {
				irc.Disconnect ();
				irc = null;
			} catch (Exception ex) {
				Logger.Log ("Exception while disconnecting from irc: {0}", ex.Message);
			}
		}

		protected override void Notify (DBWork work, DBRevisionWork revision_work, List<DBPerson> people, string message)
		{
			Logger.Log ("IrcNotification.Notify (lane_id: {1} revision_id: {2} host_id: {3} State: {0}) enabled: {4}", work.State, revision_work.lane_id, revision_work.revision_id, revision_work.host_id, enabled);

			if (!enabled)
				return;

			foreach (var person in people) {
				if (string.IsNullOrEmpty (person.irc_nicknames)) {
					using (DB db = new DB ()) {
						List<string> computed_nicks = new List<string> ();
						List<IEnumerable<string>> email_lists = new List<IEnumerable<string>> ();
						email_lists.Add (person.GetEmails (db));
						if (person.Emails != null)
							email_lists.Add (person.Emails);

						foreach (var emails in email_lists) {
							foreach (var email in emails) {
								int at = email.IndexOf ('@');
								if (at > 0) {
									computed_nicks.Add (email.Substring (0, at));
								}
							}
						}
						if (computed_nicks.Count == 0 && !string.IsNullOrEmpty (person.fullname))
							computed_nicks.Add (person.fullname);
						person.irc_nicknames = string.Join (",", computed_nicks.ToArray ());
					}
				}

				if (string.IsNullOrEmpty (person.irc_nicknames)) {
					Logger.Log ("IrcNotification: could not find somebody to notify for revision with id {0} on lane {1}", revision_work.revision_id, revision_work.lane_id);
					continue;
				}

				message = message.Replace ("{red}", "\u00034").Replace ("{bold}", "\u0002").Replace ("{default}", "\u000F");

				foreach (var nick in person.irc_nicknames.Split (',')) {
					foreach (var channel in irc.GetChannels ()) {
						irc.SendMessage (SendType.Message, channel, nick + ": " + message);
					}
				}
			}
		}

		private void Loop ()
		{
			try {
				string [] servers;
				string [] channels;
				string [] nicks;
				System.Net.IPEndPoint local_endpoint;

				servers = identity.servers.Split (',', ' ');
				channels = identity.channels.Split (',', ' ');
				nicks = identity.nicks.Split (',', ' ');

				irc.AutoRetry = true;
				irc.ActiveChannelSyncing = true;
				irc.SendDelay = 200;

				irc.OnAutoConnectError += new AutoConnectErrorEventHandler (irc_OnAutoConnectError);
				irc.OnQueryMessage += new IrcEventHandler (irc_OnQueryMessage);
				irc.OnConnected += new EventHandler (irc_OnConnected);
				irc.OnDisconnected += new EventHandler (irc_OnDisconnected);
				irc.OnRawMessage += new IrcEventHandler (irc_OnRawMessage);

				if (string.IsNullOrEmpty (Configuration.IRCLocalEndPoint)) {
					local_endpoint = null;
				} else {
					local_endpoint = new System.Net.IPEndPoint (System.Net.IPAddress.Parse (Configuration.IRCLocalEndPoint), 0);
				}

				irc.Connect (servers, 6667, local_endpoint);
				irc.Login (nicks, "MonkeyWrench");
				irc.RfcJoin (channels);
				Logger.Log ("Connected to irc: {0} joined {1} as {2}", identity.servers, identity.channels, identity.nicks);
				irc.Listen ();
			} catch (Exception ex) {
				Logger.Log ("Exception while connecting to irc: {0}", ex.Message);
			}

		}

		void irc_OnRawMessage (object sender, IrcEventArgs e)
		{
			switch (e.Data.Type) {
			case ReceiveType.ChannelMessage:
				if (e.Data.Message.StartsWith (irc.Nickname)) {
					string cmd = e.Data.Message.Substring (irc.Nickname.Length).TrimStart (':', ' ', ',');
					switch (cmd.ToLowerInvariant ()) {
					case "enable":
						enabled = true;
						break;
					case "disable":
						enabled = false;
						break;
					case "state":
						irc.SendMessage (SendType.Message, e.Data.Channel, e.Data.Nick + ": " + (enabled ? "enabled" : "disabled"));
						break;
					case "help":
					case "h":
					case "?":
					case "/?":
					case "-?":
						irc.SendMessage (SendType.Message, e.Data.Channel, e.Data.Nick + ": enable|disable: enable or disable irc notifications temporarily.");
						break;
					default:
						irc.SendMessage (SendType.Message, e.Data.Channel, e.Data.Nick + ": Don't know how to '" + cmd + "'");
						break;
					}
				}
				break;
			}
			Console.WriteLine ("OnRawMessage");
		}

		void irc_OnQueryMessage (object sender, IrcEventArgs e)
		{
			Console.WriteLine ("OnQueryMessage");
		}

		void irc_OnAutoConnectError (object sender, AutoConnectErrorEventArgs e)
		{
			Console.WriteLine ("irc_OnAutoConnectError");
		}

		void irc_OnDisconnected (object sender, EventArgs e)
		{
			Console.WriteLine ("irc_OnDisconnected");
		}

		void irc_OnConnected (object sender, EventArgs e)
		{
			Console.WriteLine ("irc_OnConnected");
		}
	}

	public class EMailNotification : NotificationBase
	{
		public EMailNotification (DBNotification notification)
			: base (notification)
		{
			/* nothing to do here really */
		}

		protected override void Notify (DBWork work, DBRevisionWork revision_work, List<DBPerson> people, string message)
		{
			throw new NotImplementedException ();
		}

		public override void Stop ()
		{
			/* Nothing to do */
		}
	}
}