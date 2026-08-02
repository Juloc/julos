using JulOS.Remote.Worker;

var worker = new RemoteWorker(TimeProvider.System);
Console.WriteLine(worker.GetType().FullName);
