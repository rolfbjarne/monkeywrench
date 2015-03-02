/*
 * Logger.cs
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
using System.Text;
using System.IO;
using System.Diagnostics;

namespace MonkeyWrench
{
	public class Logger
	{
		private readonly static int ProcessID = Process.GetCurrentProcess ().Id;

		public static bool IsVerbosityIncluded (int verbosity)
		{
			return verbosity <= Configuration.LogVerbosity;
		}

		static string FormatLog (string format, params object [] args)
		{
			string message;
			string [] lines;
			string timestamp = DateTime.Now.ToUniversalTime ().ToString ("yyyy/MM/dd HH:mm:ss.fffff UTC");

			message = string.Format (format, args);
			lines = message.Split (new char [] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < lines.Length; i++) {
				lines [i] = string.Concat ("[", ProcessID.ToString (), " - ", System.Threading.Thread.CurrentThread.ManagedThreadId.ToString (), " - ", timestamp, "] ", lines [i]);
			}
			message = string.Join ("\n", lines);
			return message + "\n";
		}

		public static void Log (string format, params object [] args)
		{
			Log (Configuration.LogVerbosity, format, args);
		}

		public static void Log (int verbosity, string format, params object [] args)
		{
			try {
				if (!IsVerbosityIncluded (verbosity))
					return;

				LogRaw (FormatLog (format, args));
			} catch (Exception ex) {
				Console.WriteLine (FormatLog ("Builder.Logger: An exception occurred while logging: {0}", ex.ToString ()));
				// This may happen if the disk is full. There's no need to do anything else.
			}
		}

		public static void LogRaw (string message)
		{
			if (string.IsNullOrEmpty (Configuration.LogFile)) {
				Console.Write (message);
			} else {
				AppendFile (Configuration.LogFile, message);
			}
		}

		static void AppendFile (string filename, string message)
		{
			var dir = Path.GetDirectoryName (filename);
			if (!Directory.Exists (dir))
				Directory.CreateDirectory (dir);

			using (FileStream fs = new FileStream (filename, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)) {
				if (fs.Length > Configuration.MaxLogSize) {
					fs.Position = 0;
					fs.SetLength (0);
				} else {
					fs.Position = fs.Length;
				}
				using (StreamWriter st = new StreamWriter (fs)) {
					st.Write (message);
				}
			}
		}
	}
}
