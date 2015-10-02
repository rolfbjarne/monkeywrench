using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace MonkeyWrench.Scheduler
{
	// This class is thread-safe.
	public class ScheduledState
	{
		object lock_obj = new object ();

	}
}

