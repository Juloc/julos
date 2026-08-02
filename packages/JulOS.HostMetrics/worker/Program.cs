using JulOS.HostMetrics.Worker;

var worker = new HostMetricsWorker(TimeProvider.System);
Console.WriteLine(worker.GetType().FullName);
