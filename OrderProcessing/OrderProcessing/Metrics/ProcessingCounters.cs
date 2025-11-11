namespace OrderProcessing.Metrics;

public static class ProcessingCounters
{
	private static int _processed;
	public static void IncrementProcessed() => Interlocked.Increment(ref _processed);
	public static int GetProcessed() => Volatile.Read(ref _processed);
}


