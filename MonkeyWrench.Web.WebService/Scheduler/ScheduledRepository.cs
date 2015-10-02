using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;

namespace MonkeyWrench.Scheduler
{
	public class ScheduledRepository
	{
		public readonly string Repository;
		public ReadOnlyCollection<ScheduledLane> Lanes;
		public string State;

		public ScheduledRepository (string repository, ReadOnlyCollection<ScheduledLane> lanes)
		{
			this.Repository = repository;
			this.Lanes = lanes;
		}

		internal void Process ()
		{
		}
	}
}

