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
using System.Threading;

namespace MonkeyWrench
{
	public interface ILogger {
		void Log (string format, params object[] args);
		void LogRaw (string message);
		void LogTo (string logName, string format, params object[] args);
		void LogToRaw (string logName, string message);
	}

	public class MainLogger : ILogger {
		public void Log (string format, params object[] args)
		{
			Logger.Log (format, args);
		}

		public void LogRaw (string message)
		{
			Logger.LogRaw (message);
		}

		public void LogTo (string logName, string format, params object[] args)
		{
			Logger.LogTo (logName, format, args);
		}

		public void LogToRaw (string logName, string message)
		{
			Logger.LogToRaw (logName, message);
		}
	}

	public class NamedLogger  : ILogger
	{
		string name;

		public NamedLogger (string logName)
		{
			this.name = logName;
		}

		public void Log (string format, params object[] args)
		{
			Logger.LogTo (name, format, args);
		}

		public void LogRaw (string message)
		{
			Logger.LogToRaw (name, message);
		}

		public void LogTo (string logName, string format, params object[] args)
		{
			Logger.LogTo (name, format, args);
			Logger.LogTo (logName, format, args);
		}

		public void LogToRaw (string logName, string message)
		{
			Logger.LogToRaw (name, message);
			Logger.LogToRaw (logName, message);
		}
	}

	public class AggregatedLogger : ILogger
	{
		IEnumerable<ILogger> logs;

		public AggregatedLogger (params ILogger[] logs)
			: this ((IEnumerable<ILogger>) logs)
		{
		}

		public AggregatedLogger (IEnumerable<ILogger> logs)
		{
			this.logs = logs;
		}

		public void Log (string format, params object[] args)
		{
			foreach (var l in logs)
				l.Log (format, args);
		}

		public void LogRaw (string message)
		{
			foreach (var l in logs)
				l.LogRaw (message);
		}

		public void LogTo (string logName, string format, params object[] args)
		{
			foreach (var l in logs)
				l.LogTo (logName, format, args);
		}

		public void LogToRaw (string logName, string message)
		{
			foreach (var l in logs)
				l.LogToRaw (logName, message);
		}
	}

	public class MemoryLogger : ILogger
	{
		StringBuilder sb = new StringBuilder ();

		public void Log (string format, params object[] args)
		{
			sb.Append (Logger.FormatLog (format, args));
		}

		public void LogRaw (string message)
		{
			sb.Append (message);
		}

		public void LogTo (string logName, string format, params object[] args)
		{
			Log (format, args);
		}

		public void LogToRaw (string logName, string message)
		{
			LogRaw (message);
		}

		public override string ToString ()
		{
			return sb.ToString ();
		}
	}

	public class Logger
	{
		private readonly static int ProcessID = Process.GetCurrentProcess ().Id;

		public static bool IsVerbosityIncluded (int verbosity)
		{
			return verbosity <= Configuration.LogVerbosity;
		}

		public static string FormatLog (string format, params object [] args)
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

		public static void LogTo (string log, string format, params object [] args)
		{
			LogTo (Configuration.LogVerbosity, log, format, args);
		}

		public static void Log (int verbosity, string format, params object [] args)
		{
			try {
				if (!IsVerbosityIncluded (verbosity))
					return;

				LogRaw (FormatLog (format, args));
			} catch (Exception ex) {
				Console.WriteLine (FormatLog ("Builder.Logger: An exception occurred while logging: {0}", ex.ToString ()));
				throw;
			}
		}

		public static void LogTo (int verbosity, string log, string format, params object [] args)
		{
			try {
				if (!IsVerbosityIncluded (verbosity))
					return;

				LogToRaw (log, FormatLog (format, args));
			} catch (Exception ex) {
				Console.WriteLine (FormatLog ("Builder.Logger: An exception occurred while logging: {0}", ex.ToString ()));
				throw;
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

		public static void LogToRaw (string log, string message)
		{
			if (string.IsNullOrEmpty (Configuration.LogDirectory)) {
				Console.Write (message);
			} else {
				AppendFile (Path.Combine (Configuration.LogDirectory, log), message);
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
