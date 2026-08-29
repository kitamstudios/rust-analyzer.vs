using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace KS.RustAnalyzer.UnitTests;

[Trait("type", "UnitTests")]
public class TraitTaxonomyTests
{
    private const string TestAssemblyPattern = "*Tests.dll";
    private const string RunnerAssemblyPattern = "KS.*Tests.dll";
    private const string TraitAttributeName = "Xunit.TraitAttribute";
    private const string TypeTrait = "type";
    private const string UnitTests = "UnitTests";
    private const string IntegrationTests = "IntegrationTests";
    private const string AcceptanceTests = "AcceptanceTests";
    private const string GateTestAssembliesVariable = "RAVS_XUNIT_TEST_ASSEMBLIES";

    private static readonly IReadOnlyList<string> TypeTraitValues = new[] { UnitTests, IntegrationTests, AcceptanceTests };

    // Invoke-Tests.ps1's own glob, restated as a matcher so the two facts below can compare the set
    // these invariants govern against the set the gate actually runs.
    private static readonly Regex RunnerAssemblyGlob = new Regex(@"^KS\..*Tests\.dll$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Test assemblies are discovered by pattern, so a new one is covered with no registration step.
    // Only the assemblies below are skipped, each for the stated reason.
    private static readonly IReadOnlyDictionary<string, string> ExcludedAssemblies =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApprovalTests.dll"] = "the ApprovalTests package ships an assertion library, not an xUnit test assembly",
        };

    private static readonly string TestAssemblyDirectory =
        Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);

    private static readonly string GateTestAssemblies =
        Environment.GetEnvironmentVariable(GateTestAssembliesVariable);

    private static readonly IReadOnlyList<string> DiscoveredTestAssemblies = DiscoverTestAssemblies();

    private static readonly IReadOnlyList<(string Name, string[] Types)> TestCases = GetTestCases();

    [Fact]
    public void DiscoveryFindsTestAssembliesAndTestCases()
    {
        DiscoveredTestAssemblies.Should().NotBeEmpty(
            "no test assembly matched {0} from {1}, which would make these invariants vacuous. Excluded: {2}",
            TestAssemblyPattern,
            string.IsNullOrWhiteSpace(GateTestAssemblies) ? TestAssemblyDirectory : GateTestAssembliesVariable,
            DescribeExclusions());

        TestCases.Should().NotBeEmpty(
            "no xUnit case was found in the discovered test assemblies, which would make these invariants vacuous: {0}",
            string.Join(", ", DiscoveredTestAssemblies.Select(Path.GetFileName)));
    }

    [Fact]
    public void EveryTestCaseCarriesExactlyOneTypeTrait()
    {
        var offenders = TestCases
            .Where(testCase => testCase.Types.Length != 1 || !TypeTraitValues.Contains(testCase.Types[0]))
            .Select(testCase => $"{testCase.Name} [{string.Join(", ", testCase.Types)}]")
            .ToArray();

        offenders.Should().BeEmpty(
            "every xUnit case must carry exactly one type trait, one of {0}",
            string.Join(", ", TypeTraitValues));
    }

    // The acceptance gate is the standalone src/TestProjects/run-integrationtests.ps1 VSTest harness, so
    // Invoke-Tests.ps1 -Mode acceptance runs no xUnit cases. An xUnit case tagged type=AcceptanceTests
    // would therefore never be executed by its own mode; keep the bucket empty until a mode runs it.
    [Fact]
    public void NoTestCaseCarriesTheAcceptanceTypeTrait()
    {
        var offenders = TestCases
            .Where(testCase => testCase.Types.Contains(AcceptanceTests))
            .Select(testCase => testCase.Name)
            .ToArray();

        offenders.Should().BeEmpty(
            "no gate mode runs xUnit type={0} cases — Invoke-Tests.ps1 -Mode acceptance runs the standalone VSTest harness. Give that mode an xUnit leg before classifying a case as {0}",
            AcceptanceTests);
    }

    // The gate supplies its exact canonical set. The local glob remains the IDE fallback.
    [Fact]
    public void EveryGovernedAssemblyIsRunByTheGate()
    {
        var offenders = DiscoveredTestAssemblies
            .Select(Path.GetFileName)
            .Where(name => !RunnerAssemblyGlob.IsMatch(name))
            .ToArray();

        offenders.Should().BeEmpty(
            "these invariants govern every {0} in {1}, but the gate runs only {2}, so an assembly matching just the first would look governed and never run",
            TestAssemblyPattern,
            TestAssemblyDirectory,
            RunnerAssemblyPattern);
    }

    [Fact]
    public void NoExcludedAssemblyIsRunByTheGate()
    {
        var offenders = ExcludedAssemblies.Keys
            .Where(name => RunnerAssemblyGlob.IsMatch(name))
            .ToArray();

        offenders.Should().BeEmpty(
            "an assembly excluded from these invariants that the gate's {0} glob still matches would run ungoverned. Excluded: {1}",
            RunnerAssemblyPattern,
            DescribeExclusions());
    }

    private static IReadOnlyList<string> DiscoverTestAssemblies()
    {
        if (!string.IsNullOrWhiteSpace(GateTestAssemblies))
        {
            return GateTestAssemblies
                .Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Path.GetFullPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Directory
            .EnumerateFiles(TestAssemblyDirectory, TestAssemblyPattern)
            .Where(path => !ExcludedAssemblies.ContainsKey(Path.GetFileName(path)))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DescribeExclusions()
    {
        return string.Join(", ", ExcludedAssemblies.Select(exclusion => $"{exclusion.Key} because {exclusion.Value}"));
    }

    private static IReadOnlyList<(string Name, string[] Types)> GetTestCases()
    {
        return DiscoveredTestAssemblies
            .SelectMany(path => Assembly.LoadFrom(path).GetTypes().Select(type => (Assembly: Path.GetFileName(path), Type: type)))
            .SelectMany(discovered => discovered.Type.GetMethods().Select(method => (discovered.Assembly, Method: method)))
            .Where(discovered => discovered.Method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Any())
            .Select(discovered => (
                Name: $"{discovered.Assembly}!{discovered.Method.DeclaringType.FullName}.{discovered.Method.Name}",
                Types: GetTraitValues(discovered.Method, TypeTrait)))
            .ToArray();
    }

    // Traits are declared on the test method and on the test class that declares it.
    private static string[] GetTraitValues(MethodInfo method, string name)
    {
        return CustomAttributeData
            .GetCustomAttributes(method)
            .Concat(CustomAttributeData.GetCustomAttributes(method.DeclaringType))
            .Where(attribute => attribute.AttributeType.FullName == TraitAttributeName)
            .Where(attribute => (string)attribute.ConstructorArguments[0].Value == name)
            .Select(attribute => (string)attribute.ConstructorArguments[1].Value)
            .ToArray();
    }
}
