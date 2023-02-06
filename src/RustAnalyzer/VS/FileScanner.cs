using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KS.RustAnalyzer.TestAdapter.Cargo;
using KS.RustAnalyzer.TestAdapter.Common;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Build;
using Microsoft.VisualStudio.Workspace.Debug;
using Microsoft.VisualStudio.Workspace.Indexing;

namespace KS.RustAnalyzer.VS;

public class FileScanner : IFileScanner
{
    private readonly IMetadataService _mds;

    public FileScanner(IMetadataService mds)
    {
        _mds = mds;
    }

    public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken)
        where T : class
    {
        var package = await _mds.GetContainingPackageAsync((PathEx)filePath, cancellationToken);
        if (package == null)
        {
            return null;
        }

        if (typeof(T) == FileScannerTypeConstants.FileDataValuesType)
        {
            var ret = GetFileDataValues(package, (PathEx)filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileDataValue>)ret);
        }
        else if (typeof(T) == FileScannerTypeConstants.FileReferenceInfoType)
        {
            var ret = GetFileReferenceInfos(package, (PathEx)filePath);
            return await Task.FromResult((T)(IReadOnlyCollection<FileReferenceInfo>)ret);
        }
        else
        {
            throw new NotImplementedException();
        }
    }

    private List<FileDataValue> GetFileDataValues(Workspace.Package package, PathEx filePath)
    {
        var allFileDataValues = new List<FileDataValue>();

        // For binaries.
        if (package.ManifestPath == filePath)
        {
            if (package.IsPackage)
            {
                foreach (var target in package.GetTargets().Where(t => t.IsRunnable))
                {
                    var launchSettings = new PropertySettings
                    {
                        [LaunchConfigurationConstants.NameKey] = target.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
                        [LaunchConfigurationConstants.ProjectKey] = (string)package.FullPath,
                        [LaunchConfigurationConstants.ProjectTargetKey] = target.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.ProgramKey] = (string)package.FullPath,
                    };

                    allFileDataValues.Add(
                        new FileDataValue(
                            type: DebugLaunchActionContext.ContextTypeGuid,
                            name: DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                            value: launchSettings,
                            target: null,
                            context: null));

                    var fileDataValuesForAllProfiles1 = Manifest.ProfileInfos.Keys.Select(
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

            var fileDataValuesForAllProfiles = Manifest.ProfileInfos.Keys.Select(
            profile =>
                new FileDataValue(
                    type: BuildConfigurationContext.ContextTypeGuid,
                    name: BuildConfigurationContext.DataValueName,
                    value: null,
                    target: null,
                    context: profile));

            allFileDataValues.AddRange(fileDataValuesForAllProfiles);
        }

        // TODO: MS: Should not need separate blocks for examples and others.

        // For examples.
        var forExamples = package.GetTargets()
            .Where(t => t.Kinds[0] == Workspace.Kind.Example)
            .Where(t => t.SourcePath == filePath)
            .SelectMany(
                t =>
                {
                    var allFileDataValues = new List<FileDataValue>();

                    var launchSettings = new PropertySettings
                    {
                        [LaunchConfigurationConstants.NameKey] = t.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.DebugTypeKey] = LaunchConfigurationConstants.NativeOptionKey,
                        [LaunchConfigurationConstants.ProjectKey] = (string)t.SourcePath,
                        [LaunchConfigurationConstants.ProjectTargetKey] = t.QualifiedTargetFileName,
                        [LaunchConfigurationConstants.ProgramKey] = (string)package.FullPath,
                    };

                    allFileDataValues.Add(
                        new FileDataValue(
                            type: DebugLaunchActionContext.ContextTypeGuid,
                            name: DebugLaunchActionContext.IsDefaultStartupProjectEntry,
                            value: launchSettings,
                            target: null,
                            context: null));

                    var fileDataValuesForAllProfiles1 = Manifest.ProfileInfos.Keys.Select(
                        profile =>
                            new FileDataValue(
                                type: BuildConfigurationContext.ContextTypeGuid,
                                name: BuildConfigurationContext.DataValueName,
                                value: null,
                                target: t.GetPath(profile),
                                context: profile));

                    allFileDataValues.AddRange(fileDataValuesForAllProfiles1);

                    var fileDataValuesForAllProfiles = Manifest.ProfileInfos.Keys.Select(
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

    private static List<FileReferenceInfo> GetFileReferenceInfos(Workspace.Package package, PathEx filePath)
    {
        var allFileRefInfos = new List<FileReferenceInfo>();

        // For binaries.
        if (package.ManifestPath == filePath && package.IsPackage)
        {
            var targets = package.GetTargets();
            var refInfos = Manifest.ProfileInfos.Keys
                .SelectMany(p => targets.Select(t => (Target: t, Profile: p)))
                .Where(x => x.Target.Kinds[0] != Workspace.Kind.Example)
                .Select(x =>
                    new FileReferenceInfo(
                        relativePath: x.Target.GetPathRelativeTo(x.Profile, filePath),
                        target: x.Target.GetPath(x.Profile),
                        context: x.Profile,
                        referenceType: (int)FileReferenceInfoType.Output));

            allFileRefInfos.AddRange(refInfos);
        }

        // TODO: MS: search for all StringComparison.OrdinalIgnoreCase.

        // TODO: MS: Should not need separate blocks for examples and others.

        // For examples.
        var forExamples = package.GetTargets()
            .Where(t => t.Kinds[0] == Workspace.Kind.Example)
            .Where(t => t.SourcePath == filePath)
            .SelectMany(t => Manifest.ProfileInfos.Keys.Select(p => (Target: t, Profile: p)))
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
