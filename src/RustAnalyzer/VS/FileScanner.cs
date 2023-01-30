using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.VS;

public class FileScanner : IFileScanner
{
    private readonly string _workspaceRoot;

    public FileScanner(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken)
        where T : class
    {
        var owningManifest = Manifest.IsManifest(filePath) ? Manifest.Create(filePath) : Manifest.GetParentManifestOrThisUnderWorkspace(_workspaceRoot, filePath);
        if (owningManifest == null)
        {
            return (T)(IReadOnlyCollection<T>)Array.Empty<T>();
        }

        if (typeof(T) == FileScannerTypeConstants.FileDataValuesType)
        {
            var ret = GetFileDataValues(owningManifest, filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileDataValue>)ret);
        }
        else if (typeof(T) == FileScannerTypeConstants.FileReferenceInfoType)
        {
            var ret = GetFileReferenceInfos(owningManifest, filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileReferenceInfo>)ret);
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    private List<FileDataValue> GetFileDataValues(Manifest owningManifest, string filePath)
    {
        var allFileDataValues = new List<FileDataValue>();

        // For binaries.
        if (owningManifest.Is(filePath))
        {
            if (owningManifest.IsPackage)
            {
                foreach (var target in owningManifest.Targets.Where(t => t.Type == TargetType.Bin))
                {
                    var launchSettings = new PropertySettings
                    {
                        [LaunchConfigurationConstants.NameKey] = target.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
                        [LaunchConfigurationConstants.ProjectKey] = owningManifest.FullPath,
                        [LaunchConfigurationConstants.ProjectTargetKey] = target.QualifiedTargetFileName,
                    };

                    allFileDataValues.Add(
                        new FileDataValue(
                            type: DebugLaunchActionContext.ContextTypeGuid,
                            name: DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                            value: launchSettings,
                            target: null,
                            context: null));

                    var fileDataValuesForAllProfiles1 = owningManifest.Profiles.Select(
                        profile =>
                            new FileDataValue(
                                type: BuildConfigurationContext.ContextTypeGuid,
                                name: BuildConfigurationContext.DataValueName,
                                value: null,
                                target: target.GetPath(profile),
                                context: profile));

                    allFileDataValues.AddRange(fileDataValuesForAllProfiles1);
                }
            }

            var fileDataValuesForAllProfiles = owningManifest.Profiles.Select(
            profile =>
                new FileDataValue(
                    type: BuildConfigurationContext.ContextTypeGuid,
                    name: BuildConfigurationContext.DataValueName,
                    value: null,
                    target: null,
                    context: profile));

            allFileDataValues.AddRange(fileDataValuesForAllProfiles);
        }

        // For examples.
        var forExamples = owningManifest.Targets
            .Where(t => t.Type == TargetType.Example)
            .Cast<ExampleTarget>()
            .Where(t => t.Source.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            .SelectMany(
                t =>
                {
                    var allFileDataValues = new List<FileDataValue>();

                    var launchSettings = new PropertySettings
                    {
                        [LaunchConfigurationConstants.NameKey] = t.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
                        [LaunchConfigurationConstants.ProjectKey] = t.Source,
                        [LaunchConfigurationConstants.ProjectTargetKey] = t.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.ProgramKey] = owningManifest.FullPath,
                    };

                    allFileDataValues.Add(
                        new FileDataValue(
                            type: DebugLaunchActionContext.ContextTypeGuid,
                            name: DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                            value: launchSettings,
                            target: null,
                            context: null));

                    var fileDataValuesForAllProfiles1 = owningManifest.Profiles.Select(
                        profile =>
                            new FileDataValue(
                                type: BuildConfigurationContext.ContextTypeGuid,
                                name: BuildConfigurationContext.DataValueName,
                                value: null,
                                target: t.GetPath(profile),
                                context: profile));

                    allFileDataValues.AddRange(fileDataValuesForAllProfiles1);

                    var fileDataValuesForAllProfiles = owningManifest.Profiles.Select(
                    profile =>
                        new FileDataValue(
                            type: BuildConfigurationContext.ContextTypeGuid,
                            name: BuildConfigurationContext.DataValueName,
                            value: null,
                            target: null,
                            context: profile));

                    allFileDataValues.AddRange(fileDataValuesForAllProfiles);
                    return allFileDataValues;
                });

        allFileDataValues.AddRange(forExamples);

        return allFileDataValues;
    }

    private static List<FileReferenceInfo> GetFileReferenceInfos(Manifest owningManifest, string filePath)
    {
        var allFileRefInfos = new List<FileReferenceInfo>();

        // For binaries.
        if (owningManifest.Is(filePath) && owningManifest.IsPackage)
        {
            var refInfos = owningManifest.Profiles
                .SelectMany(p => owningManifest.Targets.Select(t => (Target: t, Profile: p)))
                .Where(x => x.Target.Type == TargetType.Bin || x.Target.Type == TargetType.Lib)
                .Select(x =>
                    new FileReferenceInfo(
                        relativePath: x.Target.GetPathRelativeTo(x.Profile, filePath),
                        target: x.Target.GetPath(x.Profile),
                        context: x.Profile,
                        referenceType: (int)FileReferenceInfoType.Output));

            allFileRefInfos.AddRange(refInfos);
        }

        // For examples.
        var forExamples = owningManifest.Targets
            .Where(t => t.Type == TargetType.Example)
            .Cast<ExampleTarget>()
            .Where(t => t.Source.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            .SelectMany(t => owningManifest.Profiles.Select(p => (Target: t, Profile: p)))
            .Select(x =>
                new FileReferenceInfo(
                    relativePath: x.Target.GetPathRelativeTo(x.Profile, filePath),
                    target: x.Target.GetPath(x.Profile),
                    context: x.Profile,
                    referenceType: (int)FileReferenceInfoType.Output));

        allFileRefInfos.AddRange(forExamples);

        return allFileRefInfos;
    }
}
