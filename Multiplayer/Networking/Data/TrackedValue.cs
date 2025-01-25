using Multiplayer.Components.Networking;
using System;
using System.Collections.Generic;

namespace Multiplayer.Networking.Data;

public class TrackedValue<T>
{
    private T lastSentValue;
    private Func<T> valueGetter;
    private Action<T> valueSetter;
    private Func<T, T, bool> thresholdComparer;
    private bool serverAuthoritative;
    public string Key { get; }

    public TrackedValue(string key, Func<T> valueGetter, Action<T> valueSetter, Func<T, T, bool> thresholdComparer = null, bool serverAuthoritative = false)
    {
        Key = key;
        this.valueGetter = valueGetter;
        this.valueSetter = valueSetter;

        this.thresholdComparer = thresholdComparer ?? DefaultComparer;
        this.serverAuthoritative = serverAuthoritative;

        lastSentValue = valueGetter();
    }

    public bool IsDirty => thresholdComparer(CurrentValue, lastSentValue);

    public bool ServerAuthoritative => serverAuthoritative;

    public T CurrentValue
    {
        get => valueGetter();
        set
        {
            valueSetter(value);
            lastSentValue = value;
        }
    }

    public void MarkClean()
    {
        lastSentValue = CurrentValue;
    }

    public object GetValueAsObject() => CurrentValue;

    public void SetValueFromObject(object value)
    {
        if (value is T typedValue)
        {
            CurrentValue = typedValue;
        }
        else
        {
            throw new ArgumentException($"Value type mismatch. Expected {typeof(T)}, got {value.GetType()}");
        }
    }

    private bool DefaultComparer(T current, T last)
    {
        return !current.Equals(last);
    }

    public string GetDebugString()
    {
        return $"{Key}: {lastSentValue} -> {CurrentValue}";
    }

}
