using System;
using System.ComponentModel;
using System.Linq;
using KS.RustAnalyzer.Infrastructure;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.NodeEnhancements;

// TODO: RELEASE: Unit test this and FSOB.
public class NodeBrowseObjectPropertyFilter<T> : CustomTypeDescriptor
    where T : NodeBrowseObject
{
    private bool _hasTargets;
    private bool _isExecutable;
    private bool _isManifest;

    public NodeBrowseObjectPropertyFilter(T o)
        : base(TypeDescriptor.GetProvider(o).GetTypeDescriptor(o))
    {
        Object = o;
    }

    public T Object { get; }

    public void Reset(PathEx fullPath, ISettingsService ss, bool hasTargets, bool isExecutable, bool isManifest)
    {
        _hasTargets = hasTargets;
        _isExecutable = isExecutable;
        _isManifest = isManifest;
        Object.ResetForNewNode(fullPath, ss);
    }

    public override PropertyDescriptorCollection GetProperties()
    {
        return GetProperties(Array.Empty<Attribute>());
    }

    public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
    {
        var props = base
            .GetProperties(attributes)
            .Cast<PropertyDescriptor>()
            .Where(p => !SettingsService.PropertyInfo.ContainsKey(p.Name) || SettingsService.PropertyInfo[p.Name].ShouldDisplay(_hasTargets, _isExecutable, _isManifest))
            .ToArray();
        return new PropertyDescriptorCollection(props);
    }
}
