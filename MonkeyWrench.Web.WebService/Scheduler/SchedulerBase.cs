/*
 * SchedulerBase.cs
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
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

using MonkeyWrench.Database;
using MonkeyWrench.DataClasses;

namespace MonkeyWrench.Scheduler
{
	public abstract class SchedulerBase
	{
		private bool force_full_update;

		protected SchedulerBase (bool ForceFullUpdate)
		{
			force_full_update = ForceFullUpdate;
		}

		/// <summary>
		/// If the scheduler is to ignore commit reports and update everything
		/// </summary>
		public bool ForceFullUpdate
		{
			get { return force_full_update; }
		}

		protected string [] splitWithMinimumElements (string toSplit, int min)
		{
			string [] result;

			if (string.IsNullOrEmpty (toSplit))
				return new string [min];

			result = toSplit.Split (new char [] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			if (result.Length < min) {
				// Not as many elements as requested. Add more and fill new empty
				// entries with the last element of the provided entries.
				string [] tmp = new string [min];
				Array.Copy (result, tmp, min);
				for (int i = result.Length; i < tmp.Length; i++)
					tmp [i] = result [result.Length - 1];
				result = tmp;
			}

			return result;
		}

		public static List<DBLane> FilterLanes (List<DBLane> lanes, List<DBHostLane> hostlanes, string logName)
		{
			var rv = new List<DBLane> ();

			foreach (var lane in lanes) {
				if (!lane.enabled) {
					Logger.LogTo (logName, "Skipping disabled lane '{0}'", lane.lane);
				}
				// Skip lanes which aren't configured/enabled on any host completely.
				var skip_lane = true;
				for (int i = 0; i < hostlanes.Count; i++) {
					if (hostlanes [i].lane_id == lane.id && hostlanes [i].enabled) {
						skip_lane = false;
						break;
					}
				}
				if (skip_lane) {
					Logger.LogTo (logName, "Skipping lane {0}, not enabled or configured on any host.", lane.lane);
					continue;
				}
				rv.Add (lane);
			}

			return rv;
		}

	}
}
