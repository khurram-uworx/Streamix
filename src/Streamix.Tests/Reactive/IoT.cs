namespace Streamix.Tests.Reactive;

class Unsubscriber<T> : IDisposable
{
    readonly List<IObserver<T>> observers;
    readonly IObserver<T> observer;

    public Unsubscriber(List<IObserver<T>> observers, IObserver<T> observer)
        => (this.observers, this.observer) = (observers, observer);

    public void Dispose()
    {
        if (observer != null && observers.Contains(observer))
            observers.Remove(observer);
    }
}

abstract class BaseObserable<T> : IObservable<T>
{
    protected readonly List<IObserver<T>> Observers = [];

    public IDisposable Subscribe(IObserver<T> observer)
    {
        if (!Observers.Contains(observer))
            Observers.Add(observer);

        return new Unsubscriber<T>(Observers, observer);
    }
}

record TemperatureReading(string SensorId, double Temperature, DateTime Timestamp);

class TemperatureSensor : BaseObserable<TemperatureReading>
{
    readonly string sensorId;
    readonly Random random = new();

    public TemperatureSensor(string sensorId) => this.sensorId = sensorId;

    public void GenerateReading()
    {
        double temp = 20 + random.NextDouble() * 10; // Example: 20-30°C
        var reading = new TemperatureReading(sensorId, temp, DateTime.Now);

        foreach (var observer in Observers)
            observer.OnNext(reading);
    }
}

class MaxTemperatureObserver : BaseObserable<double>, IObserver<TemperatureReading>
{
    readonly TimeSpan window = TimeSpan.FromMinutes(30);
    readonly List<TemperatureReading> readings = new();

    double? lastMax = null;

    public void OnNext(TemperatureReading value)
    {
        readings.Add(value);

        var cutoff = DateTime.UtcNow - window;
        readings.RemoveAll(r => r.Timestamp < cutoff);

        double currentMax = readings.Max(r => r.Temperature);

        if (lastMax == null || lastMax != currentMax)
        {
            foreach (var obs in Observers)
                obs.OnNext(currentMax);

            lastMax = currentMax;
        }
    }

    public void OnError(Exception error)
    {
        foreach (var obs in Observers)
            obs.OnError(error);
    }

    public void OnCompleted()
    {
        foreach (var obs in Observers)
            obs.OnCompleted();
    }
}
