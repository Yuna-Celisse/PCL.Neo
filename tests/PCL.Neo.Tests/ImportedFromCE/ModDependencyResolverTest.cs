using Xunit;
using PCL.Neo.Core.Minecraft.ResourceProject;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PCL.Neo.Core.Test.Minecraft.ResourceProject;


public class ModDependencyResolverTest
{
    [Fact]
    public void ResolvesOneMissingRequiredDependency()
    {
        var resolver = new ModDependencyResolver();
        var projects = new Dictionary<string, ModDependencyProject>(StringComparer.OrdinalIgnoreCase)
        {
            ["Modrinth:B"] = new()
            {
                ProjectId = "B",
                Source = "Modrinth",
                ProjectName = "Dependency B",
                Files =
                [
                    new ModDependencyFile
                    {
                        Id = "b-file",
                        DisplayName = "Dependency B 1.20.1 Fabric",
                        Version = "1.0.0",
                        GameVersions = ["1.20.1"],
                        Loaders = ["Fabric"],
                        ReleaseType = 1,
                        ReleaseDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    },
                ],
            },
        };

        var request = new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.20.1",
            TargetLoaders = ["Fabric"],
            RequiredDependencies =
            [
                new ModDependencyReference
                {
                    ProjectId = "B",
                    Source = "Modrinth",
                },
            ],
            ProjectResolver = (source, projectId) => projects.GetValueOrDefault($"{source}:{projectId}"),
        };

        var result = resolver.Resolve(request);

        Assert.Single(result.ToInstall);
        Assert.Equal("B", result.ToInstall[0].ProjectId);
        Assert.Equal("Modrinth", result.ToInstall[0].Source);
        Assert.Equal("b-file", result.ToInstall[0].File.Id);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ReturnsEmptyForNoDependencies()
    {
        var resolver = new ModDependencyResolver();
        var request = new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.20.1",
            TargetLoaders = ["Fabric"],
            ProjectResolver = (_, _) => throw new Xunit.Sdk.XunitException("Resolver should not be invoked."),
        };

        var result = resolver.Resolve(request);

        Assert.Empty(result.ToInstall);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void ResolvesRecursiveDependencies()
    {
        var resolver = new ModDependencyResolver();
        var projects = CreateProjectStore(
            CreateProject(
                "B",
                CreateFile(
                    "b-file",
                    requiredDependencies:
                    [
                        CreateDependency("C"),
                    ])),
            CreateProject(
                "C",
                CreateFile("c-file")));

        var result = resolver.Resolve(CreateRequest(projects, CreateDependency("B")));

        Assert.Equivalent(new[] { "B", "C" }, result.ToInstall.Select(static item => item.ProjectId).ToList());
        Assert.Equal(2, result.ToInstall.Count);
        Assert.DoesNotContain(result.ToInstall, static item => item.ProjectId == "A");
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void DeduplicatesSharedDependencies()
    {
        var resolver = new ModDependencyResolver();
        var projects = CreateProjectStore(
            CreateProject(
                "B",
                CreateFile(
                    "b-file",
                    requiredDependencies:
                    [
                        CreateDependency("D"),
                    ])),
            CreateProject(
                "C",
                CreateFile(
                    "c-file",
                    requiredDependencies:
                    [
                        CreateDependency("D"),
                    ])),
            CreateProject(
                "D",
                CreateFile("d-file")));

        var result = resolver.Resolve(CreateRequest(projects, CreateDependency("B"), CreateDependency("C")));

        Assert.Equal(3, result.ToInstall.Count);
        Assert.Equivalent(new[] { "B", "C", "D" }, result.ToInstall.Select(static item => item.ProjectId).ToList());
        Assert.Equal(1, result.ToInstall.Count(static item => item.ProjectId == "D"));
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void TerminatesCycles()
    {
        var resolver = new ModDependencyResolver();
        var projects = CreateProjectStore(
            CreateProject(
                "B",
                CreateFile(
                    "b-file",
                    requiredDependencies:
                    [
                        CreateDependency("B"),
                    ])));

        var result = resolver.Resolve(CreateRequest(projects, CreateDependency("B")));

        Assert.Single(result.ToInstall);
        Assert.Equal("B", result.ToInstall[0].ProjectId);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void BlocksUnresolvedRequiredDependency()
    {
        var resolver = new ModDependencyResolver();
        var projects = new Dictionary<string, ModDependencyProject>(StringComparer.OrdinalIgnoreCase);

        var result = resolver.Resolve(CreateRequest(projects, CreateDependency("B")));

        Assert.Empty(result.ToInstall);
        Assert.Single(result.Unresolved);
        Assert.Equal("B", result.Unresolved[0].ProjectId);
        Assert.Contains("not found", result.Unresolved[0].Reason);
    }

    [Fact]
    public void SkipsAlreadyInstalledCompatibleDependency()
    {
        var resolver = new ModDependencyResolver();
        var projects = CreateProjectStore(
            CreateProject(
                "B",
                CreateFile("b-file")));

        var result = resolver.Resolve(CreateRequest(
            projects,
            [
                CreateInstalledMod("B"),
            ],
            CreateDependency("B")));

        Assert.Empty(result.ToInstall);
        Assert.Single(result.Satisfied);
        Assert.Equal("B", result.Satisfied[0].ProjectId);
        Assert.Contains("Already installed", result.Satisfied[0].Reason);
    }

    [Fact]
    public void TreatsInstalledIncompatibleDependencyAsMissing()
    {
        var resolver = new ModDependencyResolver();
        var projects = CreateProjectStore(
            CreateProject(
                "B",
                CreateFile("b-file")));

        var result = resolver.Resolve(CreateRequest(
            projects,
            [
                CreateInstalledMod("B", gameVersions: ["1.19.2"]),
            ],
            CreateDependency("B")));

        Assert.Single(result.ToInstall);
        Assert.Equal("B", result.ToInstall[0].ProjectId);
        Assert.Equal("b-file", result.ToInstall[0].File.Id);
        Assert.Empty(result.Satisfied);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void IgnoresOptionalDependencies()
    {
        var resolver = new ModDependencyResolver();
        var projects = new Dictionary<string, ModDependencyProject>(StringComparer.OrdinalIgnoreCase);

        var result = resolver.Resolve(CreateRequest(projects, CreateDependency("B", isRequired: false)));

        Assert.Empty(result.ToInstall);
        Assert.Empty(result.Unresolved);
        Assert.Single(result.Satisfied);
        Assert.Equal("B", result.Satisfied[0].ProjectId);
        Assert.Contains("Optional dependency ignored", result.Satisfied[0].Reason);
    }

    [Fact]
    public void SelectsLatestCompatibleReleaseFile()
    {
        var resolver = new ModDependencyResolver();
        var projects = CreateProjectStore(
            CreateProject(
                "B",
                CreateFile(
                    "b-alpha",
                    displayName: "Dependency B Alpha",
                    releaseType: 3,
                    releaseDate: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                CreateFile(
                    "b-beta",
                    displayName: "Dependency B Beta",
                    releaseType: 2,
                    releaseDate: new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
                CreateFile(
                    "b-release",
                    displayName: "Dependency B Release",
                    releaseType: 1,
                    releaseDate: new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc))));

        var result = resolver.Resolve(CreateRequest(projects, CreateDependency("B")));

        Assert.Single(result.ToInstall);
        Assert.Equal("b-release", result.ToInstall[0].File.Id);
        Assert.Equal(1, result.ToInstall[0].File.ReleaseType);
    }

    [Fact]
    public void DoesNotCrossMatchSources()
    {
        var resolver = new ModDependencyResolver();
        var projects = CreateProjectStore(
            CreateProject(
                "B",
                [CreateFile("b-file")],
                "CurseForge"));

        var result = resolver.Resolve(CreateRequest(
            projects,
            [
                CreateInstalledMod("B", source: "Modrinth"),
            ],
            CreateDependency("B", source: "CurseForge")));

        Assert.Single(result.ToInstall);
        Assert.Equal("B", result.ToInstall[0].ProjectId);
        Assert.Equal("CurseForge", result.ToInstall[0].Source);
        Assert.Empty(result.Satisfied);
        Assert.Empty(result.Unresolved);
    }

    private static ModDependencyRequest CreateRequest(
        Dictionary<string, ModDependencyProject> projects,
        params ModDependencyReference[] requiredDependencies)
    {
        return CreateRequest(projects, [], requiredDependencies);
    }

    private static ModDependencyRequest CreateRequest(
        Dictionary<string, ModDependencyProject> projects,
        List<InstalledModIdentity> installedMods,
        params ModDependencyReference[] requiredDependencies)
    {
        return new ModDependencyRequest
        {
            TargetMinecraftVersion = "1.20.1",
            TargetLoaders = ["Fabric"],
            RequiredDependencies = [.. requiredDependencies],
            InstalledMods = installedMods,
            ProjectResolver = (source, projectId) => projects.GetValueOrDefault($"{source}:{projectId}"),
        };
    }

    private static Dictionary<string, ModDependencyProject> CreateProjectStore(params ModDependencyProject[] projects)
    {
        var store = new Dictionary<string, ModDependencyProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            store[$"{project.Source}:{project.ProjectId}"] = project;
        }

        return store;
    }

    private static ModDependencyProject CreateProject(string projectId, params ModDependencyFile[] files)
    {
        return CreateProject(projectId, files, "Modrinth");
    }

    private static ModDependencyProject CreateProject(string projectId, ModDependencyFile[] files, string source)
    {
        return new ModDependencyProject
        {
            ProjectId = projectId,
            Source = source,
            ProjectName = $"Dependency {projectId}",
            Files = [.. files],
        };
    }

    private static ModDependencyFile CreateFile(
        string id,
        string? displayName = null,
        string? version = "1.0.0",
        List<string>? gameVersions = null,
        List<string>? loaders = null,
        int releaseType = 1,
        DateTime? releaseDate = null,
        List<ModDependencyReference>? requiredDependencies = null)
    {
        return new ModDependencyFile
        {
            Id = id,
            DisplayName = displayName ?? id,
            Version = version,
            GameVersions = gameVersions ?? ["1.20.1"],
            Loaders = loaders ?? ["Fabric"],
            ReleaseType = releaseType,
            ReleaseDate = releaseDate ?? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RequiredDependencies = requiredDependencies ?? [],
        };
    }

    private static ModDependencyReference CreateDependency(string projectId, string source = "Modrinth", bool isRequired = true)
    {
        return new ModDependencyReference
        {
            ProjectId = projectId,
            Source = source,
            IsRequired = isRequired,
        };
    }

    private static InstalledModIdentity CreateInstalledMod(
        string projectId,
        string source = "Modrinth",
        string? modId = null,
        List<string>? gameVersions = null,
        List<string>? loaders = null)
    {
        return new InstalledModIdentity
        {
            SourceProjectId = projectId,
            Source = source,
            ModId = modId ?? $"mod_{projectId.ToLowerInvariant()}",
            GameVersions = gameVersions ?? ["1.20.1"],
            Loaders = loaders ?? ["Fabric"],
        };
    }
}
