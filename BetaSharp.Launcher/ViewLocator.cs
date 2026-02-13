using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace BetaSharp.Avalonia;

internal sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? instance)
    {
        var name = instance?.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var type = Type.GetType(name);

        ArgumentNullException.ThrowIfNull(type);

        return (Control?) Activator.CreateInstance(type);
    }

    public bool Match(object? instance)
    {
        return instance is INotifyPropertyChanged;
    }
}