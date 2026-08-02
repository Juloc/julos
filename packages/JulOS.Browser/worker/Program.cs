using JulOS.Browser.Worker;

var worker = new BrowserWorker(TimeProvider.System);
Console.WriteLine(worker.GetType().FullName);
