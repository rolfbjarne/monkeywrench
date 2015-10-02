using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace MonkeyWrench.Scheduler
{
	public class ScheduledUpdate
	{
		List<int> filter_to_lanes;
		AggregatedLogger log = new AggregatedLogger ();
		ManualResetEvent completed_event = new ManualResetEvent (false);

		public DateTime ScheduledStamp;
		public DateTime StartStamp;
		public DateTime EndStamp;
		public readonly ScheduledRepository Repository;
		public bool FullUpdate { get; private set; }

		public void WaitForCompletion ()
		{
			completed_event.WaitOne ();
		}

		public void Completed ()
		{
			completed_event.Set ();
		}

		public AggregatedLogger Log {
			get { return log; }
		}

		public ReadOnlyCollection<int> FilterToLanes {
			get {
				if (filter_to_lanes == null)
					return null;
				return filter_to_lanes.AsReadOnly ();
			}
		}

		public ScheduledUpdate (string repository, bool full_update = false, IEnumerable<int> filter_to_lanes = null, ILogger extra_log = null)
		{
			Repository = new ScheduledRepository (repository, null);
			FullUpdate = full_update;
			if (filter_to_lanes != null)
				this.filter_to_lanes = new List<int> (filter_to_lanes);
			var repolog = new NamedLogger (NamedLogger.SanitizeLogName (repository) + ".log");
			this.log = new AggregatedLogger (repolog);
			if (extra_log != null)
				this.log.AddLogger (extra_log);
		}

		public void MergeWith (ScheduledUpdate update)
		{
			FullUpdate |= update.FullUpdate;
			if (update.filter_to_lanes != null) {
				if (filter_to_lanes == null) {
					filter_to_lanes = update.filter_to_lanes;
				} else {
					filter_to_lanes.AddRange (update.filter_to_lanes);
				}
			}
		}
	}

	// All members of this class are thread-safe.
	public class SchedulerQueue
	{
		// A repo can be scheduled for update when we're already updating it.
		// A repo can also be re-scheduled, then we only make sure the existing update has 'FullUpdate' set
		object lock_obj = new object ();
		Dictionary<string, ScheduledUpdate> waiting = new Dictionary<string, ScheduledUpdate> ();
		Dictionary<string, ScheduledUpdate> working = new Dictionary<string, ScheduledUpdate> ();
		AutoResetEvent evt = new AutoResetEvent (false);

		public IList<ScheduledUpdate> Waiting {
			get {
				lock (lock_obj) {
					// return a copy of the list to avoid threading issues.
					return waiting.Values.ToArray ();
				}
			}
		}

		public IList<ScheduledUpdate> Working {
			get {
				lock (lock_obj) {
					// return a copy of the list to avoid threading issues.
					return working.Values.ToArray ();
				}
			}
		}

		public void Enqueue (ScheduledUpdate update)
		{
			ScheduledUpdate existing;
			lock (lock_obj) {
				if (waiting.TryGetValue (update.Repository.Repository, out existing)) {
					existing.MergeWith (update);
				} else {
					waiting [update.Repository.Repository] = update;
					update.ScheduledStamp = DateTime.Now;
					evt.Set ();
				}
			}
		}

		// This will block until there's work.
		public ScheduledUpdate FetchWork ()
		{	
			do {
				lock (lock_obj) {
					// Find any update in 'waiting' not already in 'working'.
					foreach (var w in waiting) {
						if (working.ContainsKey (w.Key))
							continue;
						working.Add (w.Key, w.Value);
						waiting.Remove (w.Key);
						w.Value.StartStamp = DateTime.Now;
						return w.Value;
					}
				}
			} while (evt.WaitOne ());

			return null;
		}

		public void CompleteWork (ScheduledUpdate update)
		{
			lock (lock_obj) {
				working.Remove (update.Repository.Repository);
				update.EndStamp = DateTime.Now;
				evt.Set (); // there may be updates scheduled for this repo, which were ignored until now.
			}
			update.Completed ();
		}
	}
}
